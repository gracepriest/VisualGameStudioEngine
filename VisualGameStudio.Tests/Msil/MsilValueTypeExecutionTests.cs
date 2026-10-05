using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.CodeGen;
using NUnit.Framework;
using VisualGameStudio.Tests.Compiler;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

// ================================================================================================
//  Task #192 — MSIL spells a Structure, an Enum and Date/DateTime as the value types they ARE. RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. A Structure was DECLARED a class (`extends System.Object`) and every Structure or Enum was spelled `class 'T'`; Date and DateTime were not mapped at all. So a never-assigned Structure local was
//  null (a NullReferenceException on the first `p.X`), assigning or passing one aliased it instead of copying, an Enum local failed to load ("value type mismatch") or the file failed to assemble (an Enum with no
//  `As` clause declared `Int32 value__`, an ilasm syntax error — no Enum program had ever assembled on this backend), and any Date or DateTime local was "Reference to undefined class". Now `IlTypeSpec` asks
//  `IsUserValueType` and a Structure or an Enum this program declares is `valuetype 'T'` in every spec position; a Structure is `sequential ansi sealed ... extends System.ValueType`; an Enum's `value__` and
//  literals take its own underlying integer; `Color.Green` is pushed as its literal; Nothing of a value type is its default; a Structure FIELD WRITE takes the address of its storage (a local, a parameter, a field of
//  Me, a Shared field or global, `h.P.X`, `a(i).X`) and is REFUSED for any other receiver rather than written into a copy; DateTime reads Year..DayOfYear as getters and calls AddDays..AddYears with their real signatures.
//
//  ⭐ THE ORACLE IS vbc. Each probe's `Vb` is what the SDK's vbc prints for the program wrapped in a VB Module (S/t192/probes, the `.exp` beside each `.bas`; x01-x03 are S/t192/tw/xp) — never what MSIL printed. A GROUP
//  is one test case: it runs each of its probes on MSIL through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive — what a Release .blproj build and the IDE call), and
//  reports every failing cell by probe id. Where ilasm is missing the cells are SKIPPED (`TempExec.RequireTool`), never failed; the case is ignored only when no cell could run. The fixture is MSIL only: the other
//  backends already agreed with vbc on every probe here that they run at all (measured byte-for-byte before and after the fix: 11,120 emitted cells, 78 differ, all MSIL cells of value-type programs).
//  The fast `MsilValueTypeCodegenTests` below pins the emitted IL text and the refusals without ilasm.
//
//  ⛔ KNOWN GAPS — each a defect or a decision that is NOT #192's, listed with NO test (asserting one would pin the defect). All are task #282:
//    DATE LITERAL  `Dim d As Date = #1/2/2003#` does not parse, on every backend (S/t192/probes/d02).
//    ENUM MEMBER   `Color.Green` types as Object, so `Dim c As Color = Color.Green` is refused (#277) — every Enum probe here wraps its members in `CType(Color.Green, Color)` (probe e07 is the bare form).
//    ENUM PRINT    `Console.WriteLine(enumVar)` prints the member NAME where VB prints the number (C# too); a boxed Enum printed through an Object prints the name, as VB does (E17 in MsilObjectBoxingExecutionTests).
//    C++           `h.P.X = 3` through a field of Structure type is DROPPED on C++ (it prints 0): s06 is MSIL and C# only.
//    C#            `Select Case enumVar` with `Case 1` is CS0266 on C# (e05 runs on MSIL only).
//    PARSER        `Case Color.Red` is rejected by the parser.
//
//  ⭐ MUTANTS (S/t192/mut: the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped; each is killed by a committed test):
//    m1 a Structure spells `class` again (only an Enum is a value type):  the three STRUCTURE groups, every cell (TypeLoadException "value type mismatch"), and the IL pin's Structure rows.
//    m2 the Enum's underlying type from `MapType` again (`Int32 value__`):  both ENUM groups, every Enum cell (ilasm: syntax error at token 'Int32'), and the IL pin's Enum row.
//    m3 a Structure field write takes its receiver BY VALUE, not by address:  the three STRUCTURE groups (NullReferenceException; InvalidProgramException for the nested write), and the IL pin's `ldloca` row.
//
//  ⚠ Named "…ExecutionTests", but it spawns no Node and lives in the Msil namespace, which `JsExecutionTierRosterTests.RosterCoversEveryJavaScriptIntegrationFixture` does not scan — so it is in neither the roster
//    nor `NotJavaScriptExecution`. The case count is NOT in the roster's pin.
// ================================================================================================

/// <summary>
/// #192 RUN: a Structure (default, copy, ByVal, ByRef, in an array, boxed, Nothing, field writes through every receiver), an Enum (local, parameter, field, array, Select Case, CType and every underlying type)
/// and a DateTime/Date (Year..Day, AddDays, AddMonths, default and Nothing) each print vbc's answer on MSIL, through every entry point.
/// </summary>
[TestFixture]
[Category("Integration")]
public class MsilValueTypeExecutionTests
{
    // ------------------------------------------------------------------------------------------------
    // Structure
    // ------------------------------------------------------------------------------------------------

    internal static readonly TempProbe s01_structdefault = new("s01_structdefault", """
        Structure Pt
            Public X As Integer
            Public Y As Integer
        End Structure
        Sub Main()
            Dim p As Pt
            Console.WriteLine(p.X)
            Console.WriteLine(p.Y)
            p.X = 7
            p.Y = p.X + 1
            Console.WriteLine(p.X)
            Console.WriteLine(p.Y)
        End Sub
        """, "0\n0\n7\n8", Bk.Msil);

    internal static readonly TempProbe s02_structbyval = new("s02_structbyval", """
        Structure Pt
            Public X As Integer
            Public Y As Integer
        End Structure
        Sub Bump(p As Pt)
            p.X = p.X + 100
            Console.WriteLine(p.X)
        End Sub
        Sub BumpRef(ByRef p As Pt)
            p.X = p.X + 1000
        End Sub
        Sub Main()
            Dim a As Pt
            a.X = 1
            Bump(a)
            Console.WriteLine(a.X)
            BumpRef(a)
            Console.WriteLine(a.X)
            Dim b As Pt = a
            b.X = 5
            Console.WriteLine(a.X)
            Console.WriteLine(b.X)
        End Sub
        """, "101\n1\n1001\n1001\n5", Bk.Msil);

    internal static readonly TempProbe s03_structarray = new("s03_structarray", """
        Structure Pt
            Public X As Integer
            Public Y As Integer
        End Structure
        Sub Main()
            Dim a(2) As Pt
            Console.WriteLine(a(1).X)
            Dim q As Pt
            q.X = 9
            a(1) = q
            q.X = 4
            Console.WriteLine(a(1).X)
            Console.WriteLine(q.X)
        End Sub
        """, "0\n9\n4", Bk.Msil);

    internal static readonly TempProbe s04_structbox = new("s04_structbox", """
        Structure Pt
            Public X As Integer
            Public Y As Integer
        End Structure
        Sub Main()
            Dim p As Pt
            p.X = 3
            Dim o As Object = p
            p.X = 8
            Console.WriteLine(o IsNot Nothing)
            Console.WriteLine(p.X)
        End Sub
        """, "True\n8", Bk.Msil);

    internal static readonly TempProbe s05_structnothing = new("s05_structnothing", """
        Structure Pt
            Public X As Integer
            Public Y As Integer
        End Structure
        Class Holder
            Public P As Pt = Nothing
        End Class
        Sub Main()
            Dim h As New Holder()
            Console.WriteLine(h.P.X)
            Dim q As Pt = Nothing
            Console.WriteLine(q.Y)
            q.Y = 2
            q = Nothing
            Console.WriteLine(q.Y)
        End Sub
        """, "0\n0\n0", Bk.Msil);

    internal static readonly TempProbe s06_structnested = new("s06_structnested", """
        Structure Pt
            Public X As Integer
            Public Y As Integer
        End Structure
        Class Holder
            Public P As Pt
        End Class
        Dim G As Pt
        Sub Main()
            Dim h As New Holder()
            h.P.X = 3
            Console.WriteLine(h.P.X)
            Dim a(2) As Pt
            a(1).X = 5
            Console.WriteLine(a(1).X)
            G.Y = 6
            Console.WriteLine(G.Y)
        End Sub
        """, "3\n5\n6", Bk.Msil);

    // A field write through the remaining receivers the fix names: a field of Me, a Shared field and a ByRef Structure parameter.
    internal static readonly TempProbe x01_structmeshared = new("x01_structmeshared", """
        Structure Pt
            Public X As Integer
            Public Y As Integer
        End Structure
        Class Holder
            Public P As Pt
            Public Shared S As Pt
            Public Sub Poke()
                P.X = 4
                S.Y = 5
            End Sub
        End Class
        Sub SetRef(ByRef p As Pt)
            p.Y = 9
        End Sub
        Sub Main()
            Dim h As New Holder()
            h.Poke()
            Console.WriteLine(h.P.X)
            Console.WriteLine(Holder.S.Y)
            Dim loc As Pt
            SetRef(loc)
            Console.WriteLine(loc.Y)
        End Sub
        """, "4\n5\n9", Bk.Msil);

    // ------------------------------------------------------------------------------------------------
    // Enum — every member wrapped in CType: `Dim c As Color = Color.Green` is refused by the front end (#277, header)
    // ------------------------------------------------------------------------------------------------

    internal static readonly TempProbe e01_enumlocal = new("e01_enumlocal", """
        Enum Color
            Red = 1
            Green = 2
            Blue = 4
        End Enum
        Sub Main()
            Dim c As Color = CType(Color.Green, Color)
            Console.WriteLine(CInt(c))
            c = CType(Color.Blue, Color)
            Console.WriteLine(CInt(c))
            Dim d As Color
            Console.WriteLine(CInt(d))
        End Sub
        """, "2\n4\n0", Bk.Msil);

    internal static readonly TempProbe e02_enumparam = new("e02_enumparam", """
        Enum Color
            Red = 1
            Green = 2
            Blue = 4
        End Enum
        Function Code(c As Color) As Integer
            Return CInt(c) * 10
        End Function
        Function Pick(k As Integer) As Color
            If k = 0 Then Return CType(Color.Red, Color)
            Return CType(Color.Blue, Color)
        End Function
        Sub Main()
            Console.WriteLine(Code(CType(Color.Green, Color)))
            Console.WriteLine(Code(Pick(0)))
            Console.WriteLine(Code(Pick(1)))
        End Sub
        """, "20\n10\n40", Bk.Msil);

    internal static readonly TempProbe e03_enumfield = new("e03_enumfield", """
        Enum Color
            Red = 1
            Green = 2
            Blue = 4
        End Enum
        Class Car
            Public Paint As Color
        End Class
        Sub Main()
            Dim k As New Car()
            Console.WriteLine(CInt(k.Paint))
            k.Paint = CType(Color.Blue, Color)
            Console.WriteLine(CInt(k.Paint))
        End Sub
        """, "0\n4", Bk.Msil);

    internal static readonly TempProbe e04_enumarray = new("e04_enumarray", """
        Enum Color
            Red = 1
            Green = 2
            Blue = 4
        End Enum
        Sub Main()
            Dim a(2) As Color
            a(0) = CType(Color.Red, Color)
            a(2) = CType(Color.Blue, Color)
            Dim i As Integer
            For i = 0 To 2
                Console.WriteLine(CInt(a(i)))
            Next
        End Sub
        """, "1\n0\n4", Bk.Msil);

    internal static readonly TempProbe e05_enumselect = new("e05_enumselect", """
        Enum Color
            Red = 1
            Green = 2
            Blue = 4
        End Enum
        Sub Show(c As Color)
            Select Case c
                Case 1
                    Console.WriteLine("red")
                Case 2
                    Console.WriteLine("green")
                Case Else
                    Console.WriteLine("other " & CInt(c))
            End Select
        End Sub
        Sub Main()
            Show(CType(Color.Red, Color))
            Show(CType(Color.Green, Color))
            Show(CType(Color.Blue, Color))
        End Sub
        """, "red\ngreen\nother 4", Bk.Msil);

    // CType of an integer into an Enum whose underlying type is NOT Int32: a 64-bit literal (ldc.i8, conv.i8) and a Byte.
    internal static readonly TempProbe x02_enumunder = new("x02_enumunder", """
        Enum Big As Long
            Hi = 5000000000
            Lo = 1
        End Enum
        Enum Tiny As Byte
            A = 200
        End Enum
        Sub Main()
            Dim b As Big = CType(Big.Hi, Big)
            Console.WriteLine(CLng(b))
            Dim t As Tiny = CType(Tiny.A, Tiny)
            Console.WriteLine(CInt(t))
            Dim z As Big
            Console.WriteLine(CLng(z))
        End Sub
        """, "5000000000\n200\n0", Bk.Msil);

    // ------------------------------------------------------------------------------------------------
    // DateTime / Date
    // ------------------------------------------------------------------------------------------------

    internal static readonly TempProbe d01_datelocal = new("d01_datelocal", """
        Sub Main()
            Dim d As DateTime = New DateTime(2003, 1, 2)
            Console.WriteLine(d.Year)
            Console.WriteLine(d.Month)
            Console.WriteLine(d.Day)
            Dim z As DateTime
            Console.WriteLine(z.Year)
        End Sub
        """, "2003\n1\n2\n1", Bk.Msil);

    internal static readonly TempProbe d03_datearith = new("d03_datearith", """
        Sub Main()
            Dim d As DateTime = New DateTime(2003, 1, 30)
            Dim e As DateTime = d.AddDays(3)
            Console.WriteLine(e.Month)
            Console.WriteLine(e.Day)
        End Sub
        """, "2\n2", Bk.Msil);

    internal static readonly TempProbe d04_datekeyword = new("d04_datekeyword", """
        Sub Main()
            Dim d As Date = New Date(2003, 1, 2)
            Console.WriteLine(d.Year)
            Console.WriteLine(d.Month)
            Console.WriteLine(d.Day)
            Dim z As Date
            Console.WriteLine(z.Year)
        End Sub
        """, "2003\n1\n2\n1", Bk.Msil);

    // A DateTime as a field, a parameter, a return value and an array element, AddMonths (an Integer argument), and Nothing of DateTime.
    internal static readonly TempProbe x03_dateshapes = new("x03_dateshapes", """
        Class Entry
            Public Stamp As DateTime
        End Class
        Function NextDay(d As DateTime) As DateTime
            Return d.AddDays(1)
        End Function
        Sub Main()
            Dim e As New Entry()
            Console.WriteLine(e.Stamp.Year)
            e.Stamp = New DateTime(2003, 12, 31)
            Console.WriteLine(NextDay(e.Stamp).Year)
            Dim a(1) As DateTime
            Console.WriteLine(a(1).Year)
            a(0) = e.Stamp.AddMonths(2)
            Console.WriteLine(a(0).Month)
            Dim n As DateTime = Nothing
            Console.WriteLine(n.Year)
        End Sub
        """, "1\n2004\n1\n2\n1", Bk.Msil);

    // ------------------------------------------------------------------------------------------------
    // The tables
    // ------------------------------------------------------------------------------------------------

    internal static readonly IReadOnlyList<ProbeGroup> Groups = new[]
    {
        new ProbeGroup("A_Structure_defaults_to_zero_copies_on_assignment_and_ByVal_writes_through_ByRef_and_sits_in_an_array",
            new[] { s01_structdefault, s02_structbyval, s03_structarray }),
        new ProbeGroup("A_Structure_boxes_by_copy_and_Nothing_of_it_is_its_zero_value",
            new[] { s04_structbox, s05_structnothing }),
        new ProbeGroup("A_Structure_field_is_written_in_place_through_h_P_X_and_a_i_X_and_a_global_and_Me_and_Shared_and_a_ByRef_parameter",
            new[] { s06_structnested, x01_structmeshared }),
        new ProbeGroup("An_Enum_is_a_value_in_a_local_a_parameter_and_return_and_a_field",
            new[] { e01_enumlocal, e02_enumparam, e03_enumfield }),
        new ProbeGroup("An_Enum_is_a_value_in_an_array_and_a_Select_Case_and_CType_into_every_underlying_type",
            new[] { e04_enumarray, e05_enumselect, x02_enumunder }),
        new ProbeGroup("DateTime_and_Date_read_Year_Month_Day_call_AddDays_and_AddMonths_and_default_to_year_1",
            new[] { d01_datelocal, d03_datearith, d04_datekeyword, x03_dateshapes }),
    };

    private static IEnumerable<TestCaseData> GroupCells() => Groups.Select(g => new TestCaseData(g).SetName(g.Id));

    /// <summary>
    /// The table IS the proof, so its shape is pinned: a probe cannot vanish, and a group cannot lose a probe, without this test saying so. It is also the fixture's one plain [Test] besides its case source.
    /// </summary>
    [Test]
    public void TheTable_HasItsRows()
    {
        var probes = Groups.SelectMany(g => g.Probes).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(Groups.Select(g => g.Probes.Length), Is.EqualTo(new[] { 3, 2, 2, 3, 3, 4 }));
            Assert.That(probes.Select(p => p.Id), Is.EqualTo(new[]
            {
                "s01_structdefault", "s02_structbyval", "s03_structarray",
                "s04_structbox", "s05_structnothing",
                "s06_structnested", "x01_structmeshared",
                "e01_enumlocal", "e02_enumparam", "e03_enumfield",
                "e04_enumarray", "e05_enumselect", "x02_enumunder",
                "d01_datelocal", "d03_datearith", "d04_datekeyword", "x03_dateshapes",
            }));
            Assert.That(probes.Select(p => p.Id).Distinct().Count(), Is.EqualTo(probes.Count));
            Assert.That(probes.Where(p => p.Agrees != Bk.Msil).Select(p => p.Id), Is.Empty, "every probe runs on MSIL only: the header says why");
            Assert.That(probes.Sum(p => TempExec.Backends(p.Agrees).Count()), Is.EqualTo(17), "17 probes, one backend each, each through three entry points");
        });
    }

    // ============================================================================================
    // RUN — vbc's answer, on MSIL, three entry points
    // ============================================================================================

    /// <summary>
    /// Each probe of the group on MSIL through the spawned CLI (standard passes), the CLI with `--optimize` and CompileProjectFiles with the aggressive passes. A Structure spelled `class` is a
    /// TypeLoadException on every Structure cell (m1); an Enum's `Int32 value__` is an ilasm syntax error on every Enum cell (m2); a field written through a value receiver is a NullReferenceException (m3).
    /// Skipped where ilasm is missing; ignored only when no cell could run.
    /// </summary>
    [TestCaseSource(nameof(GroupCells))]
    public void AGroupOfProbes_PrintsVbcsAnswer_OnMsil(ProbeGroup group)
    {
        var failures = new List<string>();
        int ran = 0, skipped = 0;
        foreach (var probe in group.Probes)
        {
            try
            {
                TempExec.AssertMatchesInEveryEntryPoint(Bk.Msil, probe.Source, probe.Vb, probe.Id, probe.HangSafe);
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

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (ran == 0) Assert.Ignore($"{group.Id}: no ilasm on this machine ({skipped} cells skipped).");
    }
}

/// <summary>
/// #192 CODEGEN, in process — no ilasm, no spawned CLI, so it runs in the fast subset and on a machine where the execution fixture above skips. The IL text a Structure, an Enum and a DateTime
/// are spelled with, and what MSIL refuses rather than guess. Every program goes through the non-optimizing helper, the standard passes and the aggressive passes.
/// </summary>
[TestFixture]
public class MsilValueTypeCodegenTests
{
    private static IEnumerable<(string Pipeline, string Il)> Emit(string source)
    {
        yield return ("no optimizer", CompileToIl(source, optimize: false));
        yield return ("standard passes", CompileToIl(source));
        yield return ("aggressive passes", CompileToIl(source, aggressive: true));
    }

    /// <summary>The refusal's message from each pipeline, or a note that IL was emitted instead; a program that does not parse or analyse fails with its own errors (CompileToIl asserts them), never as a refusal.</summary>
    private static IEnumerable<(string Pipeline, string Message)> Refusals(string source)
    {
        var pipelines = new (string Name, Func<string> Build)[]
        {
            ("no optimizer", () => CompileToIl(source, optimize: false)),
            ("standard passes", () => CompileToIl(source)),
            ("aggressive passes", () => CompileToIl(source, aggressive: true)),
        };
        foreach (var (name, build) in pipelines)
        {
            string message;
            try { build(); message = "NOT REFUSED: MSIL emitted IL for this program"; }
            catch (ForeignFeatureException ex) { message = ex.Message; }
            yield return (name, message);
        }
    }

    /// <summary>
    /// A Structure is `valuetype`, sequential, sealed and derived from System.ValueType; its field write takes the local's ADDRESS (`ldloca`, then the value, then `stfld`); an Enum's `value__` and members are its
    /// underlying `int32`, never the IR's `Int32`; a DateTime local is the BCL value type and `d.Year` its getter. Text only: the execution fixture proves each of these RUNS, and this proves it where ilasm is not.
    /// </summary>
    [Test]
    public void AStructureAnEnumAndADateTime_AreSpelledAsValueTypes_InTheEmittedIl()
    {
        const string source = """
            Structure Pt
                Public X As Integer
                Public Y As Integer
            End Structure
            Enum Color
                Red = 1
                Green = 2
            End Enum
            Sub Main()
                Dim p As Pt
                p.X = 7
                Dim c As Color = CType(Color.Green, Color)
                Dim d As DateTime = New DateTime(2003, 1, 2)
                Console.WriteLine(p.X)
                Console.WriteLine(CInt(c))
                Console.WriteLine(d.Year)
            End Sub
            """;

        foreach (var (pipeline, il) in Emit(source))
        {
            Assert.Multiple(() =>
            {
                Assert.That(il, Does.Match(@"\.class public sequential ansi sealed (beforefieldinit )?'Pt'\s+extends \[mscorlib\]System\.ValueType"), $"{pipeline}: a Structure is declared a value type");
                Assert.That(il, Does.Contain("valuetype 'Pt' 'p'"), $"{pipeline}: a Structure local is `valuetype`, which `.locals init` zeroes");
                Assert.That(il, Does.Match(@"ldloca(\.s)?\s+\d+\s+ldc\.i4\.7\s+stfld int32 'Pt'::'X'"), $"{pipeline}: the field write is to the local's ADDRESS, not to a copy of it");
                Assert.That(il, Does.Contain("specialname rtspecialname int32 value__"), $"{pipeline}: an Enum's value__ is its underlying int32");
                Assert.That(il, Does.Not.Contain("Int32 value__"), $"{pipeline}: the IR's name for the underlying type is not an IL type");
                Assert.That(il, Does.Contain("valuetype 'Color' 'c'"), $"{pipeline}: an Enum local is `valuetype`");
                Assert.That(il, Does.Contain("valuetype [mscorlib]System.DateTime 'd'"), $"{pipeline}: a DateTime local is the BCL value type");
                Assert.That(il, Does.Contain("call instance int32 [mscorlib]System.DateTime::get_Year()"), $"{pipeline}: DateTime.Year is its getter, not an ldfld of a field it does not have");
            });
        }
    }

    /// <summary>
    /// A DateTime member outside the table is refused BY NAME, as a property (`Ticks`) and as a method (`AddTicks`): System.DateTime has no public instance fields and the ordinary call path would spell
    /// `AddTicks(int32)`, so either would assemble and then fail at run time (MissingFieldException / MissingMethodException).
    /// </summary>
    [TestCase("Console.WriteLine(d.Ticks)", "DateTime.Ticks", TestName = "A_DateTime_property_outside_the_table_is_refused_by_name")]
    [TestCase("Dim e As DateTime = d.AddTicks(5)", "DateTime.AddTicks", TestName = "A_DateTime_method_outside_the_table_is_refused_by_name")]
    public void ADateTimeMemberOutsideTheTable_IsRefusedByName(string statement, string named)
    {
        var source = $"Sub Main()\n    Dim d As DateTime = New DateTime(2003, 1, 2)\n    {statement}\n    Console.WriteLine(d.Year)\nEnd Sub";
        Assert.Multiple(() =>
        {
            foreach (var (pipeline, message) in Refusals(source))
                Assert.That(message, Does.Contain(named).And.Contain("outside the supported DateTime surface"), $"{pipeline}: {message}");
        });
    }

    /// <summary>
    /// A Structure field written through a value that is not storage — a function result (`Make().X = 3`) and a property of Structure type (`h.P.X = 3`, vbc's BC30068) — is refused, naming the Structure,
    /// instead of writing into a copy that assembles, runs and drops the write.
    /// </summary>
    [Test]
    public void AStructureFieldWrittenThroughANonVariable_IsRefused()
    {
        const string declarations = """
            Structure Pt
                Public X As Integer
                Public Y As Integer
            End Structure
            Class Holder
                Private _p As Pt
                Public Property P As Pt
                    Get
                        Return _p
                    End Get
                    Set(v As Pt)
                        _p = v
                    End Set
                End Property
            End Class
            Function Make() As Pt
                Dim r As Pt
                Return r
            End Function
            """;

        var shapes = new[]
        {
            ("a function result", "Make().X = 3"),
            ("a property of Structure type", "Dim h As New Holder()\n    h.P.X = 3"),
        };
        Assert.Multiple(() =>
        {
            foreach (var (shape, statement) in shapes)
            {
                var source = declarations + "\nSub Main()\n    " + statement + "\n    Console.WriteLine(\"done\")\nEnd Sub";
                foreach (var (pipeline, message) in Refusals(source))
                    Assert.That(message, Does.Contain("a field of Structure 'Pt' is written through a value that is not a variable"), $"{shape}, {pipeline}: {message}");
            }
        });
    }
}
