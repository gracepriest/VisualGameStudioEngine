using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #224 - what a LINQ query expression REFUSES, the fast half. `LinqQueryExpressionExecutionTests` (Integration) RUNS the shapes that are in scope on C# and JavaScript and drives the C++ / MSIL refusal
//  through the spawned CLI; this fixture has no process and no compiler: it takes each program through the two real compile routes IN PROCESS - `BasicCompiler.CompileFile` (what the CLI's single-file route
//  calls) and `BasicCompiler.CompileProjectFiles` with the aggressive passes (what a Release .blproj build and the IDE call) - because a project compile analyzes a file inside a unit, a different path from
//  the bare Parser + SemanticAnalyzer pair a unit-test helper uses.
//
//    * Group By, Join, Let, and a SECOND range variable (`From a In xs From b In ys`, `From a In xs, b In ys`) are refused with a stated message that names the clause. Before, each lowered to a free call
//      of the clause's name that no backend could run, and a second range variable read "End of statement expected, found 'From'". Aggregate (`RefuseUnloweredQueryClause`) and a String or Dictionary source
//      ("A query source must be an array, a List(Of T) or an IEnumerable(Of T)") are refused too, and are NOT pinned here.
//    * BC36533 - "'ByRef' parameter 'n' cannot be used in a query expression." - is vbc's own refusal of a ByRef parameter read inside a Where / Select / Order By clause (C# would fail with CS1628). vbc's
//      CONTROLS are accepted and stay accepted: a ByRef parameter in the From COLLECTION (probe B1), as a Take / Skip COUNT (B2), or copied to a local first (B3) - those are values, evaluated where the
//      query is built, not inside a lambda.
//    * C++ and MSIL have no LINQ in either syntax: the code generator throws the stated message for a query over a List, a query over an ARRAY (MSIL used to emit `callvirt 'Integer'::'Where'`) and the
//      method syntax (`l.Where(...)`, which C++ used to fail on in clang), through both routes.
//
//  ORACLE: vbc, for BC36533 and for what is accepted: `S/t224/vbx/X2.bas` is refused with exactly `error BC36533: 'ByRef' parameter 'n' cannot be used in a query expression.` (line 11 of the wrapped file, line 5
//  of the source) and B1, B2, B3 run (`S/t224/tools/vbvq.py`). Group By, Join, Let and a second range variable are ACCEPTED by vbc: they are refused here because BasicLang does not lower them, not because VB
//  does, which is why the message says "not supported" and names the clause.
//
//  MUTANTS (recipes `S/t224/tw/mut_tw.py`; each killed by exactly the test named): M4 `IsQueryOperatorOnSequence` always false -> `CppAndMsilRefuseAQuery...` (the generators stop throwing); M5 `CheckByRefParameterInQuery` dropped ->
//  `AByRefParameterInAClauseIsBC36533...`.
// ================================================================================================

/// <summary>#224 - the clauses a query does not lower, BC36533, and the backends with no LINQ, refused with a stated message (fast: no process, no compiler).</summary>
[TestFixture]
public class LinqQueryExpressionRefusalTests
{
    private enum Route { CompileFile, CompileProjectFiles }

    private static readonly Route[] Routes = { Route.CompileFile, Route.CompileProjectFiles };

    /// <summary>The source through one real route, in a temp directory; the result is complete before the directory goes.</summary>
    private static CompilationResult Compile(string source, Route route)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t224-fast-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            return route == Route.CompileFile
                ? new BasicCompiler(new CompilerOptions()).CompileFile(path)
                : new BasicCompiler(new CompilerOptions { OptimizeAggressive = true }).CompileProjectFiles(new List<string> { path });
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static string Said(CompilationResult result)
        => string.Join(" | ", result.AllErrors.Select(e => $"{e.Line}: {e.ErrorCode} {e.Message}"));

    // ---------------------------------------------------------------------------------------------
    // Clauses the lowering does not build
    // ---------------------------------------------------------------------------------------------

    private const string GroupBy = """
        Sub Main()
            Dim xs As New List(Of Integer)()
            xs.Add(3)
            Dim q = From x In xs Group By x Into g = Group
            Console.WriteLine(1)
        End Sub
        """;

    private const string Join = """
        Sub Main()
            Dim xs As New List(Of Integer)()
            xs.Add(3)
            Dim ys As New List(Of Integer)()
            ys.Add(3)
            Dim q = From x In xs Join y In ys On x Equals y Select x + y
            Console.WriteLine(1)
        End Sub
        """;

    private const string Let = """
        Sub Main()
            Dim xs As New List(Of Integer)()
            xs.Add(3)
            Dim q = From x In xs Let y = x * 2 Select y
            Console.WriteLine(1)
        End Sub
        """;

    private const string SecondRangeVariable = """
        Sub Main()
            Dim xs As New List(Of Integer)()
            xs.Add(3)
            Dim ys As New List(Of Integer)()
            ys.Add(1)
            Dim q = From a In xs From b In ys Select a + b
            Console.WriteLine(1)
        End Sub
        """;

    /// <summary>
    /// A clause the lowering does not build is refused with a message that NAMES it (and the supported ones), on both routes, and no program results. `Join` also reports a cascaded
    /// "Undefined identifier" for its range variable AFTER the stated message; only the stated message is pinned (see the execution fixture's known gaps).
    /// </summary>
    [TestCase(GroupBy, "The 'Group By' query clause is not supported", TestName = "AGroupByClause_IsRefused_ByName")]
    [TestCase(Join, "The 'Join' query clause is not supported", TestName = "AJoinClause_IsRefused_ByName")]
    [TestCase(Let, "The 'Let' query clause is not supported", TestName = "ALetClause_IsRefused_ByName")]
    [TestCase(SecondRangeVariable, "A query with more than one range variable", TestName = "ASecondRangeVariable_IsRefused_ByName")]
    public void AnUnsupportedQueryClause_IsRefused_WithAMessageThatNamesIt(string source, string stated)
    {
        Assert.Multiple(() =>
        {
            foreach (var route in Routes)
            {
                var result = Compile(source, route);
                Assert.That(result.HasErrors, Is.True, $"{route}: the program must be refused");
                Assert.That(result.AllErrors.Select(e => e.Message), Has.Some.Contain(stated), $"{route}: {Said(result)}");
                Assert.That(result.AllErrors.Select(e => e.Message), Has.None.Contain("End of statement expected"),
                    $"{route}: a second range variable must not read as a statement-end error. {Said(result)}");
            }
        });
    }

    // ---------------------------------------------------------------------------------------------
    // BC36533
    // ---------------------------------------------------------------------------------------------

    private const string ByRefInAClause = """
        Function CountAbove(ByRef n As Integer) As Integer
            Dim xs As New List(Of Integer)()
            xs.Add(1)
            xs.Add(9)
            Dim q = From v In xs Where v > n Select v
            Return q.Count()
        End Function

        Sub Main()
            Dim a As Integer = 4
            Console.WriteLine(CountAbove(a))
        End Sub
        """;

    /// <summary>vbc's B1: the ByRef parameter is read in the From COLLECTION (a call's argument), outside any clause lambda.</summary>
    private const string ByRefInTheCollection = """
        Function MakeList(k As Integer) As List(Of Integer)
            Dim xs As New List(Of Integer)()
            xs.Add(k)
            xs.Add(k + 1)
            Return xs
        End Function

        Function F(ByRef n As Integer) As Integer
            Dim q = From v In MakeList(n) Select v
            Return q.Count()
        End Function

        Sub Main()
            Dim a As Integer = 4
            Console.WriteLine(F(a))
        End Sub
        """;

    /// <summary>vbc's B2: the ByRef parameter is a Take COUNT.</summary>
    private const string ByRefAsTheTakeCount = """
        Function F(ByRef n As Integer) As Integer
            Dim xs As New List(Of Integer)()
            xs.Add(1)
            xs.Add(2)
            xs.Add(3)
            Dim q = From v In xs Take n
            Return q.Count()
        End Function

        Sub Main()
            Dim a As Integer = 2
            Console.WriteLine(F(a))
        End Sub
        """;

    /// <summary>vbc's B3: the ByRef parameter is copied to a local, and the clause reads the local.</summary>
    private const string ByRefCopiedToALocal = """
        Function F(ByRef n As Integer) As Integer
            Dim xs As New List(Of Integer)()
            xs.Add(1)
            xs.Add(5)
            Dim m As Integer = n
            Dim q = From v In xs Where v > m Select v
            Return q.Count()
        End Function

        Sub Main()
            Dim a As Integer = 2
            Console.WriteLine(F(a))
        End Sub
        """;

    /// <summary>
    /// ⭐ M5. A ByRef parameter read inside a Where clause is vbc's BC36533, at the parameter's own reference (line 5), on both routes - and the analyzer's coded error, not a C# CS1628 later. The three
    /// shapes vbc ACCEPTS (the collection, a Take count, a local copy) still compile: BC36533 is about the lambda a clause lowers to, not about the query.
    /// </summary>
    [Test]
    public void AByRefParameterInAClauseIsBC36533_ButNotInTheCollectionATakeCountOrACopy()
    {
        Assert.Multiple(() =>
        {
            foreach (var route in Routes)
            {
                var refused = Compile(ByRefInAClause, route);
                var coded = refused.AllErrors.Where(e => e.ErrorCode == "BC36533").ToList();
                Assert.That(coded, Has.Count.EqualTo(1), $"{route}: one BC36533. {Said(refused)}");
                if (coded.Count == 1)
                {
                    Assert.That(coded[0].Message, Does.Contain("'ByRef' parameter 'n' cannot be used in a query expression."), $"{route}");
                    Assert.That(coded[0].Line, Is.EqualTo(5), $"{route}: the line of the clause that reads n");
                }

                foreach (var (name, source) in new[]
                         {
                             ("the From collection (B1)", ByRefInTheCollection),
                             ("a Take count (B2)", ByRefAsTheTakeCount),
                             ("a local copy (B3)", ByRefCopiedToALocal),
                         })
                {
                    var accepted = Compile(source, route);
                    Assert.That(accepted.HasErrors, Is.False, $"{route}: vbc accepts a ByRef parameter as {name}. {Said(accepted)}");
                }
            }
        });
    }

    // ---------------------------------------------------------------------------------------------
    // C++ and MSIL: no LINQ in either syntax
    // ---------------------------------------------------------------------------------------------

    private const string QueryOverAList = """
        Sub Main()
            Dim xs As New List(Of Integer)()
            xs.Add(3)
            Dim q = From x In xs Where x > 2 Select x * 10
            For Each v In q
                Console.WriteLine(v)
            Next
        End Sub
        """;

    private const string QueryOverAnArray = """
        Sub Main()
            Dim arr() As Integer = {3, 1, 4}
            Dim q = From x In arr Where x > 2 Select x * 10
            For Each v In q
                Console.WriteLine(v)
            Next
        End Sub
        """;

    private const string MethodSyntax = """
        Sub Main()
            Dim xs As New List(Of Integer)()
            xs.Add(3)
            Dim q = xs.Where(Function(x As Integer) x > 2)
            Console.WriteLine(q.Count())
        End Sub
        """;

    /// <summary>
    /// ⭐ M4. C++ and MSIL refuse a query over a List, a query over an ARRAY and the method syntax with the LINQ message, from the code generator - before a clang or an ilasm is asked - for the IR each
    /// real route builds (standard and aggressive passes). With M4 the generators emit `xs->Where(f)` / `callvirt 'Integer'::'Where'` instead and nothing throws.
    /// </summary>
    [Test]
    public void CppAndMsilRefuseAQuery_AndTheMethodSyntax_WithTheLinqMessage_OnBothRoutes()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, source, operatorName) in new[]
                     {
                         ("a query over a List", QueryOverAList, "Where"),
                         ("a query over an array", QueryOverAnArray, "Where"),
                         ("the method syntax", MethodSyntax, "Where"),
                     })
            {
                foreach (var route in Routes)
                {
                    var result = Compile(source, route);
                    Assert.That(result.HasErrors, Is.False, $"{name}, {route}: the front end accepts it. {Said(result)}");
                    var ir = result.CombinedIR;
                    Assert.That(ir, Is.Not.Null, $"{name}, {route}: no IR");
                    if (ir == null) continue;

                    var cpp = Assert.Catch(() => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(ir),
                        $"{name}, {route}: C++ must refuse");
                    Assert.That(cpp, Is.TypeOf<CppCapabilityException>(), $"{name}, {route}: C++");
                    Assert.That(cpp?.Message, Does.Contain($"the LINQ operator '{operatorName}' is not available on the C++ backend"), $"{name}, {route}: C++");

                    var msil = Assert.Catch(() => new MSILCodeGenerator().Generate(ir), $"{name}, {route}: MSIL must refuse");
                    Assert.That(msil?.Message, Does.Contain($"the LINQ operator '{operatorName}' is not available on the MSIL backend"), $"{name}, {route}: MSIL");
                }
            }
        });
    }
}
