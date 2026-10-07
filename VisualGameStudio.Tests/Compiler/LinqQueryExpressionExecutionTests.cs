using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #224, RUN. A LINQ query expression (`From x In xs Where x > 2 Select x * 10`) used to fail on every backend: the IR builder evaluated each clause's expression once, in the creator, and handed
//  the value to a free call named after the clause (`Select(Where(xs, x > 2), x * 10)`) - CS0103 on C#, a ReferenceError on JavaScript, a refusal on MSIL, a build failure on C++. The front end also
//  typed a List's range variable as Object (`x * 10` was refused), never parsed `Order By`, and typed the query as an array (`Return From ...` was refused in a Function As IEnumerable(Of T)).
//
//  Now a query lowers to the IR the method syntax already produces - `xs.Where(Function(x) x > 2).Select(Function(x) x * 10)`, an IRInstanceMethodCall chain whose clause expressions are lambdas over the
//  range variable (`IRBuilder.BuildLambda`, shared with a written lambda). IN SCOPE: one `From` over a 1-D array, a `List(Of T)` or an `IEnumerable(Of T)`, then `Where` / `Select` / `Order By [Descending]` /
//  `Take` / `Skip` / `Distinct`. It RUNS on C# (System.Linq, lazy like VB) and on JavaScript (Array methods, eager). It is REFUSED, with a stated diagnostic, for `Group By`, `Join`, `Aggregate`, `Let`, a
//  second range variable, a String or Dictionary source and a ByRef parameter inside a clause (VB's BC36533): `LinqQueryExpressionRefusalTests`, the fast half. C++ and MSIL have no LINQ in EITHER syntax
//  and refuse a query and the method syntax alike, with one stated message: the last test here drives that through the real CLI and a Release .blproj build.
//
//  ORACLE: vbc. Every program below is the implementer's probes (`S/t224/probes`: Q02-Q05, Q09-Q12, Q16, E04, E10, E14, E16) joined into one program per test and printed one line per shape through a
//  `Show` helper; the expected text is the output of the SDK's vbc on the program wrapped in a VB Module with `Imports System.Linq` (`S/t224/tools/vbvq.py`), never a backend's. The test-writer measured
//  every program through vbc before writing the row.
//
//  ENTRY POINTS: each row goes through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive), on C# and JavaScript: `TempExec.AssertMatchesInEveryEntryPoint`; and through
//  `CompileProjectFiles` with .NET resolution ARMED (`CompilerOptions.EnableNetResolution()`), which is how the CLI's `build` and the IDE's BuildService call it and how `TempExec`'s project leg does NOT: the
//  unarmed leg refuses a List passed as an IEnumerable(Of T) argument (`Argument 2: cannot convert from 'List<Integer>' to 'IEnumerable<Integer>'`, accepted by the CLI and by a .blproj build), so no row passes one.
//  ⛔ Every row is `HangSafe` (a C# leg runs in a child process with a time limit, `CSharpProcessRunner`, #256). A missing Node SKIPS the JavaScript cells; the C# cells always run.
//  ⚠ Named "...ExecutionTests" and runs JavaScript under Node: it is in `JsExecutionTierRosterTests`' roster.
//
//  MUTANTS (each built from a plain source copy of the fix with ONE change, its BasicLang.dll swapped into a copy of the test output; recipes `S/t224/tw/mut_tw.py`). The tests that go RED, measured:
//    M1 the range variable is not reserved in the creator (`ReserveInCurrentFunction(range.Name)` dropped) -> `NameReservationTests` `LinqRange_variable`, `TempMintingDoorTests.ALinqRangeVariable_IsNeverMinted` (x2)
//       and `TempMintingFacilityTests.ALinqRangeVariable_NeverTakesAMintedName_AndTheProgramRunsOnCSharpAndJavaScript`; NOT this fixture (no row spells a temp-shaped name).
//    M2 `Order By` and `Order By ... Descending` swapped (OrderBy / OrderByDescending) -> exactly `OrderBy_AscendingAndDescending_AreNotSwapped` and `AUserClassQuery_ReadsMembers_InWhereSelectAndOrderBy`
//    M3 an ARRAY is not a JavaScript LINQ source (`linqSource` admits a List only) -> exactly `AnArrayQuery_WhereAndSelectOfATypedExpression_PrintsVbcsAnswer` and `AFunctionReturningAQuery_AsIEnumerable_OverAListAndAnArray`
//       (the JavaScript cells: the array has no `Where`)
//    M4 `ForeignFeatureChecker.IsQueryOperatorOnSequence` always false -> C++ and MSIL stop refusing: exactly `CppAndMsil_RefuseAQuery_WithTheLinqMessage_ThroughTheCliAndAReleaseBuild` here and
//       `LinqQueryExpressionRefusalTests.CppAndMsilRefuseAQuery_AndTheMethodSyntax_WithTheLinqMessage_OnBothRoutes`. C# and JavaScript keep running every row (measured: the C# backend's System.Linq using does not hang on it).
//    M5 BC36533 dropped (`CheckByRefParameterInQuery`) -> exactly `LinqQueryExpressionRefusalTests.AByRefParameterInAClauseIsBC36533_ButNotInTheCollectionATakeCountOrACopy`
//
//  ⛔ KNOWN GAPS - listed, deliberately NOT tested (asserting one would pin a defect, or the shape is outside the scope above):
//    * JavaScript is EAGER, like the method syntax already was (`JavaScriptGenericsLinqTests.Linq_IsEager_NotDeferred`): a source mutated AFTER the query and BEFORE it is enumerated (probe E03) prints the old
//      result on JavaScript, where C# and VB are lazy and print the new one.
//    * The method syntax's RESULT is typed Object on C# (`xs.Where(Function(x As Integer) x > 2).Select(...)` assigned to a `Dim q`: CS1579, probes M01 / M03), so only the query spelling is run here.
//    * `Select New With {.A = x, .B = x * 2}` does not parse (probe Q08).
//    * A ByRef argument inside a NESTED call in a clause is CS1620 on C# (#232), outside what BC36533 catches.
//    * `Join` is refused first with its stated message, then a cascaded "Undefined identifier" for the join's range variable is reported after it (measured, `LinqQueryExpressionRefusalTests` asserts the
//      stated message only).
// ================================================================================================

/// <summary>#224 - a LINQ query expression over an array, a List(Of T) or an IEnumerable(Of T), on C# and JavaScript; C++ and MSIL refuse it.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the Node, C# child and spawned-CLI runs share the machine
public class LinqQueryExpressionExecutionTests
{
    /// <summary>Every query row runs on exactly these two backends: they are the ones that run a query.</summary>
    private const Bk CSharpAndJavaScript = Bk.CSharp | Bk.JavaScript;

    /// <summary>`Show` prints a sequence on ONE line, so one row can print several shapes, one per line.</summary>
    private const string Show = """
        Sub Show(label As String, s As IEnumerable(Of Integer))
            Dim r As String = label & ":"
            For Each v In s
                r = r & " " & v
            Next
            Console.WriteLine(r)
        End Sub

        """;

    /// <summary>
    /// One row on C# and JavaScript, each through every entry point. A failing cell is collected, not thrown, so the other still reports and the
    /// failure text names the row, the backend and the entry point. The C# cells always run; a missing Node skips the JavaScript ones.
    /// </summary>
    private static void AssertRow(TempProbe row)
    {
        var failures = new List<string>();
        int ran = 0, skipped = 0;
        foreach (var backend in TempExec.Backends(row.Agrees))
        {
            try
            {
                TempExec.RequireTool(backend);
            }
            catch (IgnoreException)
            {
                skipped++;
                continue;
            }

            ran++;
            try
            {
                TempExec.AssertMatchesInEveryEntryPoint(backend, row.Source, row.Vb, row.Id, row.HangSafe);
            }
            catch (AssertionException ex)
            {
                failures.Add(ex.Message);
            }

            try
            {
                var got = TempExec.Norm(TempExec.Run(backend, EmitViaArmedProject(backend, row.Source), row.HangSafe));
                if (got != TempExec.Norm(row.Vb))
                    failures.Add($"{row.Id} on {backend}, through CompileProjectFiles with .NET resolution armed: printed [{got.Replace("\n", " | ")}] where VB prints [{TempExec.Norm(row.Vb).Replace("\n", " | ")}]");
            }
            catch (AssertionException ex)
            {
                failures.Add($"{row.Id} on {backend}, through CompileProjectFiles with .NET resolution armed: {ex.Message.Split('\n')[0]}");
            }
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (ran == 0) Assert.Ignore($"no execution tool on this machine ({skipped} cells skipped).");
    }

    /// <summary>
    /// <c>CompileProjectFiles</c> with the aggressive passes and .NET resolution armed - what a Release <c>.blproj</c> build and the IDE's BuildService call - to the backend's generated text.
    /// Asserts the compile succeeded.
    /// </summary>
    private static string EmitViaArmedProject(Bk backend, string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t224-armed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            var options = new CompilerOptions { OptimizeAggressive = true, TargetBackend = TempExec.TargetName(backend) };
            options.EnableNetResolution();
            var result = new BasicCompiler(options).CompileProjectFiles(new List<string> { path });
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the project entry point produced no combined IR");
            return backend switch
            {
                Bk.CSharp => new ImprovedCSharpCodeGenerator().Generate(result.CombinedIR),
                Bk.JavaScript => new JavaScriptCodeGenerator().Generate(result.CombinedIR),
                _ => throw new ArgumentException(backend.ToString()),
            };
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static TempProbe Row(string id, string source, string vb) => new(id, source, vb, CSharpAndJavaScript, HangSafe: true);

    // ---------------------------------------------------------------------------------------------
    // The rows. Each source's expected text is vbc's, one `label: values` line per shape.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// (1) M3. A query over an ARRAY: `From x In arr Where x > 2 Select x * 10` (probe Q09). The range variable is the array's Integer, so `x * 10` types; before, the array's query
    /// was the only one whose range variable was typed at all. On JavaScript an array is a LINQ source only because the lowering admits it (M3 drops that: `arr.Where is not a function`).
    /// </summary>
    [Test]
    public void AnArrayQuery_WhereAndSelectOfATypedExpression_PrintsVbcsAnswer()
        => AssertRow(Row("R1_Array", Show + """
            Sub Main()
                Dim arr() As Integer = {3, 1, 4, 1, 5}
                Dim q = From x In arr Where x > 2 Select x * 10
                Show("array", q)
            End Sub
            """, "array: 30 40 50"));

    /// <summary>
    /// (2) The same over a `List(Of Integer)` (probe Q03; before, a List's range variable was Object and `x * 10` was refused), the clause reading an OUTER local (`x > t`, probe Q14 - the lambda
    /// captures it), and the CONTROL: the same result computed with a `For Each` over the same list. The query and the loop print one line each, and the captured local is untouched.
    /// </summary>
    [Test]
    public void AListQuery_ReadsAnOuterLocal_AndMatchesTheForEachControl()
        => AssertRow(Row("R2_List", Show + """
            Sub Main()
                Dim xs As New List(Of Integer)()
                xs.Add(3)
                xs.Add(1)
                xs.Add(4)
                xs.Add(1)
                xs.Add(5)
                Dim t As Integer = 2
                Dim q = From x In xs Where x > t Select x * 10
                Show("query", q)
                Dim r As String = "loop:"
                For Each v In xs
                    If v > t Then
                        r = r & " " & (v * 10)
                    End If
                Next
                Console.WriteLine(r)
                Console.WriteLine(t)
            End Sub
            """, "query: 30 40 50\nloop: 30 40 50\n2"));

    /// <summary>
    /// (3) M3. A query RETURNED from a Function As IEnumerable(Of Integer) (probes Q12, E16): `Return From x In xs Where x > 2 Select x`. The front end typed a query as an array, so this was refused.
    /// A List source and an ARRAY source, the array parameter being the JavaScript M3 shape again.
    /// </summary>
    [Test]
    public void AFunctionReturningAQuery_AsIEnumerable_OverAListAndAnArray()
        => AssertRow(Row("R3_Function", Show + """
            Function Big(xs As List(Of Integer)) As IEnumerable(Of Integer)
                Return From x In xs Where x > 2 Select x
            End Function

            Function BigArr(a() As Integer) As IEnumerable(Of Integer)
                Return From x In a Where x > 5 Select x
            End Function

            Sub Main()
                Dim xs As New List(Of Integer)()
                xs.Add(3)
                xs.Add(1)
                xs.Add(4)
                xs.Add(1)
                xs.Add(5)
                Dim arr() As Integer = {4, 8, 15}
                Show("list", Big(xs))
                Show("array", BigArr(arr))
            End Sub
            """, "list: 3 4 5\narray: 8 15"));

    /// <summary>
    /// (4) M2. `Order By x` and `Order By x Descending` (probes Q04, Q05), and a Where, a descending Order By and a Select of an expression in one query (Q16). Never parsed before. M2 swaps
    /// OrderBy and OrderByDescending: the first two lines print each other's text.
    /// </summary>
    [Test]
    public void OrderBy_AscendingAndDescending_AreNotSwapped()
        => AssertRow(Row("R4_OrderBy", Show + """
            Sub Main()
                Dim xs As New List(Of Integer)()
                xs.Add(3)
                xs.Add(1)
                xs.Add(4)
                xs.Add(1)
                xs.Add(5)
                Dim asc = From x In xs Order By x Select x
                Dim desc = From x In xs Order By x Descending Select x
                Dim mixed = From x In xs Where x > 1 Order By x Descending Select x * 2
                Show("asc", asc)
                Show("desc", desc)
                Show("mixed", mixed)
            End Sub
            """, "asc: 1 1 3 4 5\ndesc: 5 4 3 1 1\nmixed: 10 8 6"));

    /// <summary>
    /// (5) `Distinct`, `Skip 1 Take 2` and `Take 3` with no Select (probe E04), and `.ToList()` / `.ToArray()` / `.Count()` on a query's result (probes Q11, E14: a `.Count()` of the
    /// query, a `List.Count`, an element of the List, an array's `Length` and an element). On C# these need System.Linq, which the backend imports when the IR calls a query operator on a sequence.
    /// </summary>
    [Test]
    public void TakeSkipDistinct_AndToListToArrayCount_OnTheResult()
        => AssertRow(Row("R5_TakeSkipDistinctMaterialise", Show + """
            Sub Main()
                Dim xs As New List(Of Integer)()
                xs.Add(3)
                xs.Add(1)
                xs.Add(4)
                xs.Add(1)
                xs.Add(5)
                Dim d = From x In xs Distinct
                Dim s = From x In xs Skip 1 Take 2
                Dim t = From x In xs Take 3
                Show("distinct", d)
                Show("skip-take", s)
                Show("take", t)
                Dim q = From x In xs Where x > 2 Select x
                Dim l = q.ToList()
                Dim a = q.ToArray()
                Console.WriteLine(q.Count() & " " & l.Count & " " & l(0) & " " & a.Length & " " & a(2))
            End Sub
            """, "distinct: 3 1 4 5\nskip-take: 1 4\ntake: 3 1 4\n3 3 3 3 5"));

    /// <summary>
    /// (6) M2. A query over a List of a USER class reading its fields (probes Q10, E10): `Where p.Age > 30 Select p.Name` (a String result), `Order By p.Name Select p.Age` and
    /// `Order By p.Age Descending Select p.Name`. The range variable is typed as the class, so `p.Age` is an Integer in the comparison.
    /// </summary>
    [Test]
    public void AUserClassQuery_ReadsMembers_InWhereSelectAndOrderBy()
        => AssertRow(Row("R6_UserClass", """
            Class Person
                Public Name As String
                Public Age As Integer
                Public Sub New(n As String, a As Integer)
                    Name = n
                    Age = a
                End Sub
            End Class

            Sub ShowI(label As String, s As IEnumerable(Of Integer))
                Dim r As String = label & ":"
                For Each v In s
                    r = r & " " & v
                Next
                Console.WriteLine(r)
            End Sub

            Sub ShowS(label As String, s As IEnumerable(Of String))
                Dim r As String = label & ":"
                For Each v In s
                    r = r & " " & v
                Next
                Console.WriteLine(r)
            End Sub

            Sub Main()
                Dim people As New List(Of Person)()
                people.Add(New Person("Cy", 35))
                people.Add(New Person("Ann", 40))
                people.Add(New Person("Bob", 20))
                Dim names = From p In people Where p.Age > 30 Select p.Name
                Dim ages = From p In people Order By p.Name Select p.Age
                Dim oldest = From p In people Order By p.Age Descending Select p.Name
                ShowS("names", names)
                ShowI("ages", ages)
                ShowS("oldest", oldest)
            End Sub
            """, "names: Cy Ann\nages: 40 20 35\noldest: Ann Cy Bob"));

    // ---------------------------------------------------------------------------------------------
    // C++ and MSIL: no LINQ in either syntax, refused with ONE stated message - through the real entry points.
    // ---------------------------------------------------------------------------------------------

    private const string RefusedQuery = """
        Sub Main()
            Dim xs As New List(Of Integer)()
            xs.Add(3)
            xs.Add(1)
            Dim q = From x In xs Where x > 2 Select x * 10
            For Each v In q
                Console.WriteLine(v)
            Next
        End Sub
        """;

    private static string TempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t224-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteQuietly(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
    }

    /// <summary>
    /// (7) M4. A query on C++ and on MSIL is refused with the LINQ message - before, C++ emitted `xs->Where(f)` (a clang error) and MSIL `callvirt 'Integer'::'Where'` (garbage IL, an ilasm error). Through
    /// the spawned CLI (`--target=cpp` / `--target=msil`, standard and `--optimize`: exit 1, the message, and NO output file written) and through a Release `.blproj` build, which on C++ reports the
    /// capability checker's BL6001 before any compiler is asked. No compiler or assembler is needed to see any of it. The CONTROL: the same program is accepted by `--target=csharp`.
    /// </summary>
    [Test]
    public void CppAndMsil_RefuseAQuery_WithTheLinqMessage_ThroughTheCliAndAReleaseBuild()
    {
        var failures = new List<string>();

        foreach (var (target, extension, backendName) in new[] { ("cpp", ".cpp", "C++"), ("msil", ".il", "MSIL") })
        {
            foreach (var optimize in new[] { false, true })
            {
                var dir = TempDirectory();
                try
                {
                    File.WriteAllText(Path.Combine(dir, "Prog.bas"), RefusedQuery);
                    var args = new List<string> { "Prog.bas", "--target=" + target };
                    if (optimize) args.Add("--optimize");
                    var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
                    var output = stdout + stderr;
                    var label = $"CLI --target={target}{(optimize ? " --optimize" : "")}";
                    if (exit == 0) failures.Add($"{label}: exited 0, it must refuse.\n{output}");
                    if (!output.Contains($"the LINQ operator 'Where' is not available on the {backendName} backend"))
                        failures.Add($"{label}: no LINQ refusal message.\n{output}");
                    if (File.Exists(Path.Combine(dir, "Prog" + extension)))
                        failures.Add($"{label}: wrote Prog{extension} for a program it refused.");
                }
                finally { DeleteQuietly(dir); }
            }

            var projectDir = TempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(projectDir, "Main.bas"), RefusedQuery);
                File.WriteAllText(Path.Combine(projectDir, "App.blproj"),
                    $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <BasicLangProject Version="1.0">
                      <PropertyGroup>
                        <ProjectName>App</ProjectName>
                        <OutputType>Exe</OutputType>
                        <TargetBackend>{(target == "cpp" ? "Cpp" : "MSIL")}</TargetBackend>
                      </PropertyGroup>
                      <ItemGroup>
                        <Compile Include="Main.bas" />
                      </ItemGroup>
                    </BasicLangProject>
                    """);
                var (exit, stdout, stderr) = CliTestHarness.RunProcess(
                    CliTestHarness.CliPath(), new[] { "build", Path.Combine(projectDir, "App.blproj"), "-c", "Release" }, projectDir, timeoutMs: 180_000);
                var output = stdout + "\n" + stderr;
                var label = $"build -c Release ({backendName})";
                if (exit == 0) failures.Add($"{label}: exited 0, it must refuse.\n{output}");
                if (!output.Contains($"the LINQ operator 'Where' is not available on the {backendName} backend"))
                    failures.Add($"{label}: no LINQ refusal message.\n{output}");
                if (target == "cpp" && !output.Contains("BL6001"))
                    failures.Add($"{label}: the capability checker's BL6001 is not reported (a compiler was asked instead?).\n{output}");
                if (output.Contains("clang") || output.Contains("ilasm"))
                    failures.Add($"{label}: the output names a compiler or assembler, so the refusal came too late.\n{output}");
            }
            finally { DeleteQuietly(projectDir); }
        }

        var control = TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(control, "Prog.bas"), RefusedQuery);
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), new[] { "Prog.bas", "--target=csharp" }, control, timeoutMs: 120_000);
            if (exit != 0) failures.Add($"CLI --target=csharp (the control): the same program must compile.\n{stdout}{stderr}");
        }
        finally { DeleteQuietly(control); }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }
}
