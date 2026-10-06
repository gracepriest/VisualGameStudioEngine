using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #212, RUN. A VB conversion intrinsic of a statically OBJECT operand is VB's own runtime on C# and MSIL: CInt(o) is Conversions.ToInteger(object), as vbc emits it.
//
//  ⛔ THE BUG. `CInt(o)`, `CLng`, `CDbl`, `CSng`, `CBool`, `CShort`, `CByte`, `CSByte`, `CUShort`, `CUInt`, `CULng` (and `CType(o, Integer/Long/Double/Boolean)`, which lowers to the same intrinsics) of an Object lowered to
//  `System.Convert.ToXxx(object)` — .NET's rules, not VB's. Measured against vbc, on C# and MSIL alike, from a green build: a boxed True through CInt was 1 (vbc: -1, and 255 / 65535 / ... through CByte / CUShort / ...); CBool of a
//  boxed "0" or "12" THREW FormatException (vbc: False / True); CInt of a boxed " 3.5 " threw (vbc parses it: 4); CInt of a boxed Char printed its code, 65 (vbc throws InvalidCastException); and CInt of a boxed "abc" threw
//  FormatException where vbc throws InvalidCastException.
//  The fix: C# gives an Object source the same `Microsoft.VisualBasic.CompilerServices.Conversions.ToXxx` text it already gave a String source (`CSharpBackend.VbConversionText`), passing the Nothing LITERAL as `(object)null`
//  because a bare `null` binds the `(string)` overload and `ToBoolean((string)null)` throws where CBool(Nothing) is False. MSIL's `EmitConvertFromObject` calls `[Microsoft.VisualBasic.Core]...Conversions::ToXxx(object)` for the
//  eleven numeric and Boolean targets (`VbObjectConversions`), through the conditional `.assembly extern Microsoft.VisualBasic.Core` that ADR-0012's late-bound comparison added.
//  ⚠ CStr is DELIBERATELY unchanged, and stays `Convert.ToString(object)` on both: VB's CStr of Nothing is Nothing where Convert gives "", `Len` lowers to `.Length`, and `&` converts an Object operand through CStr — so adopting VB's
//  turns a running `Len(CStr(o))` into a NullReferenceException (the `CStr_…` case below is that fence).
//
//  ⭐ THE ORACLE IS vbc. Each probe's `Vb` is what the SDK's vbc prints for the program wrapped in a VB Module (S/t212/probes, probes2: the `.exp` beside each `.bas`; S/t212/tw/probes for the three the test-writer added — T07, N08,
//  CS1). Every `.exp` was RE-RUN through vbc by the test-writer (S/t212/tw/vb/build.sh) and matched. Never what a backend printed.
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the IR optimizer"): every probe runs on C# AND on MSIL, each through the real CLI (standard passes), the real CLI with `--optimize`
//  (aggressive) and `BasicCompiler.CompileProjectFiles` with `OptimizeAggressive` (what a Release .blproj and the IDE call): `TempExec.Run`. ⛔ Every C# RUN is HANG-SAFE (`CSharpProcessRunner`, a child process with a time limit).
//  MSIL needs ilasm: where it is missing the MSIL legs are SKIPPED, the C# legs still run and still FAIL the case, and the case is reported Ignored only when every C# leg passed. A failure in one probe or leg is collected, not
//  thrown, so every other one still reports and the message names the probe, the backend and the entry point.
//  The FAST half is `ObjectConversionIntrinsicShapeTests` below: what the compiler WRITES (no process) — the table of all eleven targets on both backends, the Nothing literal's own rule, and the CONTROL that a typed source emits no VB
//  runtime call. The MSIL text of CInt/CLng/CDbl/CSng/CBool and CStr, with the `.assembly extern` line, is `MsilObjectBoxingTests.ConversionIntrinsic_OfAnObjectOperand_CallsVbConversionsToXxx_…`.
//  The moved #177 pins that now hold #212's answers are in `MsilObjectBoxingExecutionTests` (`CIntOfBoxedTrue_…_PrintMinusOne_…`, `E14_…_EndInVbcsUnhandledInvalidCast_…`).
//
//  ⭐ MUTANTS (each is the fix plus ONE change, built from a plain source copy of the fix outside the worktree and run against a copy of the test output with its BasicLang.dll swapped; the cases that go red, measured):
//    M1 the C# Object arm removed (an Object source keeps `Convert.ToXxx`)    -> TWELVE: `ABoxedBoolean_…`, `ABoxedTrue_…`, `CBool_OfABoxedString_…`, `CInt_OfAPaddedStringAndOfAHalfwayDouble_…`, `AnUnconvertibleBoxedValue_…`,
//                                                                                `CType_OfAnObject_…`, `AnObjectReturnedByAFunction_…`, the text cases `AnObjectConversion_OnCSharp_…`, `TypedConversions_…` (the one Object `CInt` is gone) and
//                                                                                `TheNothingLiteral_OnCSharp_…`, and the moved pins `CIntOfBoxedTrue_…` and `E14_…`. Not the Nothing / CStr / ordinary-Integer cases: Convert answers those the same.
//    M2 a bare `null` for the Nothing literal (C# only)                        -> TWO, `Nothing_AsALiteralAndInsideAnObject_…` (`Conversions.ToBoolean((string)null)` throws on the run) and the text case
//                                                                                `TheNothingLiteral_OnCSharp_…` (the one that is in the fast subset). Nothing else.
//    M3 the MSIL `VbObjectConversions` table never consulted                   -> SIXTEEN: the same run cases as M1 (MSIL legs), `AnObjectConversion_OnMsil_…`, `TypedConversions_…`, the five rows of
//                                                                                `MsilObjectBoxingTests.ConversionIntrinsic_OfAnObjectOperand_CallsVbConversionsToXxx_…`, and the moved pins `CIntOfBoxedTrue_…` and `E14_…`.
//    M4 the MSIL call kept but the `.assembly extern` never declared           -> SEVEN, ALL TEXT: `AnObjectConversion_OnMsil_…`, `TypedConversions_…` (its one-Object leg) and the five `ConversionIntrinsic_…` rows. ⛔ No RUN case
//                                                                                can see it: ilasm on Linux INFERS the reference to a type it can find (ADR-0012), so the module still assembles and prints right.
//    M5 C#: CStr of an Object adopts VB's `Conversions.ToString`               -> TWO, `CStr_OfAnObject_StaysOnConvert_…` (`Len(CStr(n))` of a Nothing is a NullReferenceException) and the text case
//                                                                                `AnObjectConversion_OnCSharp_…`.
//    M5b MSIL: the same, `["ToString"] = "ToString"` added to the table        -> THREE, `CStr_OfAnObject_StaysOnConvert_…` (MSIL legs), `AnObjectConversion_OnMsil_…` and `MsilObjectBoxingTests.CStrOfAnObjectOperand_StaysConvertToString_…`.
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect, or a defect that is another task's). Each is the same before and after #212:
//    * CStr of an Object: `CStr(Nothing)` is "" here where VB's is Nothing (`Dim s As String = CStr(o) : s Is Nothing` is False, vbc: True), a Date's text and a class instance's differ too. The follow-up is its own task; the fence
//      this fixture does hold is that `Len(CStr(o))` and `"[" & CStr(o) & "]"` keep working.
//    * `CType(o, Short)`, `CType(o, Byte)` and the other narrow targets are not the intrinsic: they lower to an IRCast UNBOX (`(short)(o)` / `unbox.any`), which THROWS for a boxed Integer where vbc converts (7, 255).
//      Measured in S/t212/fu/F1.
//    * JavaScript's Object conversions disagree with VB (J01 `CInt` of a boxed True prints 1, J02 `CBool("0")` is True, J06 `CType`), and JavaScript has no `CLng` lowering at all. This fixture is C# and MSIL only.
//    * MSIL's typed Boolean `CInt` / `CLng` / `CDbl` print 1 (probe C01; vbc: -1) — the typed path, not the Object one — and `CBool` of a typed Double or Integer is refused on MSIL (probe C02, "'CBool' is called, but it is
//      neither a procedure this program declares…").
//    * `CDec` is not a registered intrinsic on any backend (probe O14).
//    * An IMPLICIT Object-to-Integer assignment (`Dim i As Integer = o`, probe O11) is refused by the front end on every backend; vbc narrows it late-bound.
//    * C++ has no Object at all ("Object has no C++ mapping"), so there is nothing to convert there.
//
//  ⚠ Named "…ExecutionTests" but it spawns no Node: it is C# and MSIL only, so it is listed in JsExecutionTierRosterTests.NotJavaScriptExecution, and NOT in the roster.
// ================================================================================================

/// <summary>The #212 probe programs and vbc's answer for each. C# and MSIL; every C# run is hang-safe.</summary>
internal static class ObjectConversionProbes
{
    private static TempProbe P(string id, string source, string vb) => new(id, source, vb, Bk.CSharp | Bk.Msil, HangSafe: true);

    /// <summary>O01 — a boxed True and False through the signed and floating conversions. True converts to -1 (all bits set), False to 0; CBool and CStr of them keep their answers. (Before: CInt printed 1.)</summary>
    internal static readonly TempProbe O01 = P("O01_boxed_boolean_signed_and_float", """
        Sub Main()
            Dim t As Object = True
            Dim f As Object = False
            Console.WriteLine(CInt(t))
            Console.WriteLine(CLng(t))
            Console.WriteLine(CDbl(t))
            Console.WriteLine(CSng(t))
            Console.WriteLine(CShort(t))
            Console.WriteLine(CStr(t))
            Console.WriteLine(CBool(t))
            Console.WriteLine(CInt(f))
            Console.WriteLine(CDbl(f))
            Console.WriteLine(CStr(f))
        End Sub
        """, "-1\n-1\n-1\n-1\n-1\nTrue\nTrue\n0\n0\nFalse");

    /// <summary>O13 — a boxed True through CByte and the unsigned conversions: all bits set, so the type's maximum (CSByte: -1). (Before: every one of them printed 1.)</summary>
    internal static readonly TempProbe O13 = P("O13_boxed_true_byte_and_unsigned", """
        Sub Main()
            Dim t As Object = True
            Console.WriteLine(CByte(t))
            Console.WriteLine(CUShort(t))
            Console.WriteLine(CUInt(t))
            Console.WriteLine(CULng(t))
            Console.WriteLine(CSByte(t))
        End Sub
        """, "255\n65535\n4294967295\n18446744073709551615\n-1");

    /// <summary>O02 — a boxed "0" converts to 0 and CBool of it is False (Convert.ToBoolean threw FormatException), through every numeric conversion.</summary>
    internal static readonly TempProbe O02 = P("O02_boxed_zero_string", """
        Sub Main()
            Dim o As Object = "0"
            Console.WriteLine(CBool(o))
            Console.WriteLine(CInt(o))
            Console.WriteLine(CDbl(o))
            Console.WriteLine(CLng(o))
            Console.WriteLine(CShort(o))
            Console.WriteLine(CByte(o))
            Console.WriteLine(CStr(o))
        End Sub
        """, "False\n0\n0\n0\n0\n0\n0");

    /// <summary>O03 — a boxed "12": CBool is True (any non-zero number), and the numeric conversions parse it and take part in arithmetic.</summary>
    internal static readonly TempProbe O03 = P("O03_boxed_twelve_string", """
        Sub Main()
            Dim o As Object = "12"
            Console.WriteLine(CInt(o))
            Console.WriteLine(CBool(o))
            Console.WriteLine(CDbl(o) / 8)
            Console.WriteLine(CLng(o) * 3)
            Console.WriteLine(CByte(o))
            Console.WriteLine(CStr(o))
        End Sub
        """, "12\nTrue\n1.5\n36\n12\n12");

    /// <summary>O04 — a boxed "True" and "false": CBool reads the word, case-insensitively, as it did before #212 (the control: the rows that already ran right still do).</summary>
    internal static readonly TempProbe O04 = P("O04_boxed_boolean_word", """
        Sub Main()
            Dim o As Object = "True"
            Dim p As Object = "false"
            Console.WriteLine(CBool(o))
            Console.WriteLine(CBool(p))
            Console.WriteLine(CStr(o))
        End Sub
        """, "True\nFalse\nTrue");

    /// <summary>O05 — a boxed " 3.5 " (padded): VB's parser trims it and CInt ROUNDS it, 4; CDbl is 3.5; CBool is True. (Before: CInt, CDbl and CLng threw FormatException.)</summary>
    internal static readonly TempProbe O05 = P("O05_boxed_padded_decimal_string", """
        Sub Main()
            Dim o As Object = " 3.5 "
            Console.WriteLine(CInt(o))
            Console.WriteLine(CDbl(o))
            Console.WriteLine(CLng(o))
            Console.WriteLine(CSng(o))
            Console.WriteLine(CBool(o))
            Console.WriteLine(CShort(o))
        End Sub
        """, "4\n3.5\n4\n3.5\nTrue\n4");

    /// <summary>O06 — a boxed 2.5 and 3.5 through CInt: ROUND-half-to-even, 2 and 4 (not truncation); the same through CLng, CShort and CByte; CBool True; CStr 3.5.</summary>
    internal static readonly TempProbe O06 = P("O06_boxed_double_halfway", """
        Sub Main()
            Dim a As Object = 2.5
            Dim b As Object = 3.5
            Console.WriteLine(CInt(a))
            Console.WriteLine(CInt(b))
            Console.WriteLine(CLng(a))
            Console.WriteLine(CLng(b))
            Console.WriteLine(CShort(b))
            Console.WriteLine(CByte(a))
            Console.WriteLine(CBool(a))
            Console.WriteLine(CStr(b))
            Console.WriteLine(CDbl(a) * 2)
        End Sub
        """, "2\n4\n2\n4\n4\n2\nTrue\n3.5\n5");

    /// <summary>
    /// T07 — a value that cannot convert THROWS, as vbc does, never reads bits: a boxed Char through CInt, CLng and CDbl (before: CInt printed the code 65), and a boxed "abc" (before: a FormatException). vbc's exception is an
    /// InvalidCastException, which is a SystemException and NOT a FormatException, so each handler is `Catch FormatException` then `Catch SystemException` and the program prints which one caught. (The exact type is the moved E14 pin's:
    /// MSIL accepts neither `Catch ex As InvalidCastException` — not one of its 12 recognized names — nor `ex.GetType()`, MissingMethodException: two unrelated limits.) CStr of the Char still answers "A".
    /// </summary>
    internal static readonly TempProbe T07 = P("T07_unconvertible_throws_not_format", """
        Sub Main()
            Dim o As Object = "A"c
            Console.WriteLine(CStr(o))
            Try
                Console.WriteLine(CInt(o))
            Catch ex As FormatException
                Console.WriteLine("format")
            Catch ex As SystemException
                Console.WriteLine("system")
            End Try
            Try
                Console.WriteLine(CLng(o))
            Catch ex As FormatException
                Console.WriteLine("format")
            Catch ex As SystemException
                Console.WriteLine("system")
            End Try
            Try
                Console.WriteLine(CDbl(o))
            Catch ex As FormatException
                Console.WriteLine("format")
            Catch ex As SystemException
                Console.WriteLine("system")
            End Try
            Dim s As Object = "abc"
            Try
                Console.WriteLine(CInt(s))
            Catch ex As FormatException
                Console.WriteLine("format")
            Catch ex As SystemException
                Console.WriteLine("system")
            End Try
        End Sub
        """, "A\nsystem\nsystem\nsystem\nsystem");

    /// <summary>
    /// O15 — the Nothing LITERAL through the intrinsics: CBool(Nothing) is False, CInt and CDbl 0. ⛔ This is M2's killer: the C# backend passes the literal as `(object)null`, because a bare `null` binds
    /// `Conversions.ToBoolean(string)`, and `ToBoolean((string)null)` THROWS. (The literal is a constant in the IR, so an optimizer that folded it would hide the call; both pipelines run it.)
    /// </summary>
    internal static readonly TempProbe O15 = P("O15_nothing_literal", """
        Sub Main()
            Console.WriteLine(CBool(Nothing))
            Console.WriteLine(CInt(Nothing))
            Console.WriteLine(CDbl(Nothing))
        End Sub
        """, "False\n0\n0");

    /// <summary>N08 — an Object HOLDING Nothing through the numeric and Boolean conversions: 0 and False. (No CStr here: that is the `CStr_…` case's.)</summary>
    internal static readonly TempProbe N08 = P("N08_object_holding_nothing", """
        Sub Main()
            Dim o As Object = Nothing
            Console.WriteLine(CInt(o))
            Console.WriteLine(CLng(o))
            Console.WriteLine(CDbl(o))
            Console.WriteLine(CBool(o))
            Console.WriteLine(CByte(o))
        End Sub
        """, "0\n0\n0\nFalse\n0");

    /// <summary>O10 — `CType(o, T)` to Integer, Long, Double and Boolean is the same conversion as the intrinsic (a boxed True is -1; a boxed "0" is False and 0).</summary>
    internal static readonly TempProbe O10 = P("O10_ctype_of_an_object", """
        Sub Main()
            Dim o As Object = True
            Dim z As Object = "0"
            Console.WriteLine(CType(o, Integer))
            Console.WriteLine(CType(o, Long))
            Console.WriteLine(CType(o, Double))
            Console.WriteLine(CType(z, Boolean))
            Console.WriteLine(CType(z, Integer))
        End Sub
        """, "-1\n-1\n-1\nFalse\n0");

    /// <summary>
    /// CS1 — CStr of an Object stays on `Convert.ToString`. ⛔ This is M5's killer: with VB's `Conversions.ToString`, CStr(Nothing) is Nothing and `Len(CStr(n))` (which lowers to `.Length`) is a NullReferenceException; here it is 0,
    /// and the `&` of it is "[]". A boxed True, 2.5 and "12" keep their text.
    /// </summary>
    internal static readonly TempProbe CS1 = P("CS1_cstr_of_an_object_stays_convert", """
        Sub Main()
            Dim n As Object = Nothing
            Console.WriteLine("[" & CStr(n) & "]")
            Console.WriteLine(Len(CStr(n)))
            Dim t As Object = True
            Dim d As Object = 2.5
            Dim s As Object = "12"
            Console.WriteLine(CStr(t) & CStr(d) & CStr(s))
        End Sub
        """, "[]\n0\nTrue2.512");

    /// <summary>O09 — the ORDINARY case, a boxed Integer 7, through every conversion: nothing that worked before may move (CInt(o) + 1, CBool, CDbl(o) / 2, CByte, CShort, CLng).</summary>
    internal static readonly TempProbe O09 = P("O09_boxed_integer_every_conversion", """
        Sub Main()
            Dim o As Object = 7
            Console.WriteLine(CInt(o) + 1)
            Console.WriteLine(CBool(o))
            Console.WriteLine(CDbl(o) / 2)
            Console.WriteLine(CStr(o))
            Console.WriteLine(CByte(o))
            Console.WriteLine(CShort(o))
            Console.WriteLine(CLng(o))
        End Sub
        """, "8\nTrue\n3.5\n7\n7\n7\n7");

    /// <summary>O12 — the operand is a FUNCTION RESULT typed Object (never a literal-initialised local, which an optimizer could see through): a boxed True through CInt / CLng, a boxed "0" through CBool / CDbl.</summary>
    internal static readonly TempProbe O12 = P("O12_function_result_object", """
        Function MakeT() As Object
            Return True
        End Function
        Function MakeS() As Object
            Return "0"
        End Function
        Sub Main()
            Console.WriteLine(CInt(MakeT()))
            Console.WriteLine(CLng(MakeT()) * 2)
            Console.WriteLine(CBool(MakeS()))
            Console.WriteLine(CDbl(MakeS()) + 1)
        End Sub
        """, "-1\n-2\nFalse\n1");

    /// <summary>O16 — a function that returns Nothing on one path and "1" on the other: `CBool(Pick(0))` False, `CBool(Pick(1))` True, `CInt(Pick(1)) + CInt(Pick(0))` 1 (the Nothing arm adds 0).</summary>
    internal static readonly TempProbe O16 = P("O16_function_result_nothing_or_text", """
        Function Pick(n As Integer) As Object
            If n = 0 Then
                Return Nothing
            End If
            Return "1"
        End Function
        Sub Main()
            Console.WriteLine(CBool(Pick(0)))
            Console.WriteLine(CBool(Pick(1)))
            Console.WriteLine(CInt(Pick(1)) + CInt(Pick(0)))
        End Sub
        """, "False\nTrue\n1");

    // ---- the sources of the fast shape tests -----------------------------------------------------

    /// <summary>The conversions the Object arm covers: the intrinsic, the VB runtime method it becomes, and the MSIL result type.</summary>
    internal static readonly (string Intrinsic, string VbMethod, string ResultSpec)[] Table =
    {
        ("CInt", "ToInteger", "int32"),
        ("CLng", "ToLong", "int64"),
        ("CDbl", "ToDouble", "float64"),
        ("CSng", "ToSingle", "float32"),
        ("CBool", "ToBoolean", "bool"),
        ("CByte", "ToByte", "uint8"),
        ("CShort", "ToShort", "int16"),
        ("CSByte", "ToSByte", "int8"),
        ("CUShort", "ToUShort", "uint16"),
        ("CUInt", "ToUInteger", "uint32"),
        ("CULng", "ToULong", "uint64"),
    };

    /// <summary>One Object operand through every row of <see cref="Table"/> and through CStr. Built FROM the table, so a row added to it is a row this program converts.</summary>
    internal static readonly string EveryTarget =
        "Function MakeO() As Object\n Return 5\nEnd Function\n" +
        "Sub Main()\n Dim o As Object = MakeO()\n" +
        string.Concat(Table.Select(row => $" Console.WriteLine({row.Intrinsic}(o))\n")) +
        " Console.WriteLine(CStr(o))\nEnd Sub\n";

    /// <summary>The CONTROL, typed only: Integer and Double operands through the conversions. No Object, so no VB runtime call and no `.assembly extern`.</summary>
    internal const string TypedOnly = """
        Function MakeI() As Integer
            Return 7
        End Function
        Function MakeD() As Double
            Return 2.5
        End Function
        Sub Main()
            Dim i As Integer = MakeI()
            Dim d As Double = MakeD()
            Console.WriteLine(CInt(d))
            Console.WriteLine(CLng(i))
            Console.WriteLine(CDbl(i))
            Console.WriteLine(CSng(d))
            Console.WriteLine(CByte(i))
        End Sub
        """;

    /// <summary>The same typed conversions beside exactly ONE Object conversion (`CInt(o)`): only that one may call the VB runtime, so the count is exactly one — a rule that sent the typed ones there fails it, and so does one that
    /// never sent the Object one.</summary>
    internal const string TypedBesideOneObject = """
        Function MakeI() As Integer
            Return 7
        End Function
        Function MakeD() As Double
            Return 2.5
        End Function
        Function MakeO() As Object
            Return 20
        End Function
        Sub Main()
            Dim i As Integer = MakeI()
            Dim d As Double = MakeD()
            Dim o As Object = MakeO()
            Console.WriteLine(CInt(d))
            Console.WriteLine(CLng(i))
            Console.WriteLine(CDbl(i))
            Console.WriteLine(CSng(d))
            Console.WriteLine(CByte(i))
            Console.WriteLine(CInt(o))
        End Sub
        """;

    /// <summary>The Nothing literal through two intrinsics. M2's TEXT: each call must be passed `(object)null`.</summary>
    internal const string NothingLiteral = """
        Sub Main()
            Console.WriteLine(CBool(Nothing))
            Console.WriteLine(CInt(Nothing))
        End Sub
        """;
}

/// <summary>#212 — a conversion of an Object is VB's own runtime on C# and MSIL, RUN against vbc through every entry point.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the CLI legs spawn dotnet and the runners spawn children; keep the machine to this fixture
public class ObjectConversionIntrinsicExecutionTests
{
    /// <summary>The message of a failed assertion without the emitted program (which can be long): the first lines, up to the "--- emitted ---" marker. A compile failure keeps its Roslyn diagnostic.</summary>
    private static string Brief(string message)
        => string.Join(" // ", message.Replace("\r\n", "\n").Split('\n').TakeWhile(l => !l.StartsWith("--- emitted") && !l.StartsWith("--- generated")).Take(4));

    /// <summary>Whether ilasm is on this machine. <c>RequireIlasm</c> skips with an IgnoreException; the MSIL legs ask instead, so the C# legs of the same case still run.</summary>
    private static bool MsilAvailable()
    {
        try
        {
            MsilHarness.RequireIlasm();
            return true;
        }
        catch (IgnoreException)
        {
            return false;
        }
    }

    /// <summary>
    /// Each probe on C# and on MSIL, through the CLI, the CLI with <c>--optimize</c> and <c>CompileProjectFiles</c>: each must print vbc's answer. A failure in one probe, backend or entry point is collected, not thrown.
    /// MSIL legs are skipped without ilasm; the case is then Ignored, but only after every C# leg passed.
    /// </summary>
    private static void AssertPrintsVbcsAnswer(params TempProbe[] probes)
    {
        var msil = MsilAvailable();
        var failures = new List<string>();
        foreach (var probe in probes)
            foreach (var backend in TempExec.Backends(probe.Agrees))
            {
                if (backend == Bk.Msil && !msil) continue;
                foreach (var entry in Enum.GetValues<EntryPoint>())
                {
                    try
                    {
                        var got = TempExec.Norm(TempExec.Run(backend, entry, probe.Source, hangSafe: true));
                        if (got != TempExec.Norm(probe.Vb))
                            failures.Add($"{probe.Id} {backend} {entry}: printed [{got.Replace("\n", " | ")}] where vbc prints [{TempExec.Norm(probe.Vb).Replace("\n", " | ")}]");
                    }
                    catch (AssertionException ex)
                    {
                        failures.Add($"{probe.Id} {backend} {entry}: {Brief(ex.Message)}");
                    }
                }
            }

        Assert.That(failures, Is.Empty, $"on C# and MSIL, through {string.Join(", ", Enum.GetValues<EntryPoint>())}:\n" + string.Join("\n", failures));
        if (!msil) Assert.Ignore("No ilasm on this machine: the C# legs passed, the MSIL legs were skipped.");
    }

    /// <summary>(1) A boxed True converts to -1 through CInt / CLng / CDbl / CSng / CShort, a boxed False to 0. (Before: 1 — Convert.ToInt32(object).) M1 and M3 (the C# arm / the MSIL table gone) fail it.</summary>
    [Test]
    public void ABoxedBoolean_ConvertsToMinusOne_NotOne_ThroughTheSignedAndFloatConversions()
        => AssertPrintsVbcsAnswer(ObjectConversionProbes.O01);

    /// <summary>(2) A boxed True through CByte / CUShort / CUInt / CULng / CSByte is all bits set: 255, 65535, 4294967295, 18446744073709551615, -1. (Before: 1 on each.) Reaches the five MSIL table rows the first case does not.</summary>
    [Test]
    public void ABoxedTrue_ConvertsToAllOnes_ThroughByteAndTheUnsignedConversions()
        => AssertPrintsVbcsAnswer(ObjectConversionProbes.O13);

    /// <summary>(3) CBool of a boxed "0" is False and of a boxed "12" True, and of "True" / "false" the word. (Before: "0" and "12" threw FormatException.)</summary>
    [Test]
    public void CBool_OfABoxedString_ParsesAsVbDoes()
        => AssertPrintsVbcsAnswer(ObjectConversionProbes.O02, ObjectConversionProbes.O03, ObjectConversionProbes.O04);

    /// <summary>(4) CInt of a boxed " 3.5 " is 4 (VB's parser trims and rounds; before, FormatException) and of a boxed 2.5 / 3.5 is 2 / 4: round-half-to-even, not truncation.</summary>
    [Test]
    public void CInt_OfAPaddedStringAndOfAHalfwayDouble_Rounds_AsVbDoes()
        => AssertPrintsVbcsAnswer(ObjectConversionProbes.O05, ObjectConversionProbes.O06);

    /// <summary>(5) CInt of a boxed Char throws InvalidCastException (before: it printed 65), and CInt of a boxed "abc" throws InvalidCastException too (before: FormatException, which the program's handler does not catch).</summary>
    [Test]
    public void AnUnconvertibleBoxedValue_ThrowsInvalidCast_NotACodeOrAFormatException()
        => AssertPrintsVbcsAnswer(ObjectConversionProbes.T07);

    /// <summary>(6) The Nothing literal and an Object holding Nothing convert to 0 and False. ⛔ M2 (a bare `null` for the literal) fails it: `Conversions.ToBoolean((string)null)` throws on C#.</summary>
    [Test]
    public void Nothing_AsALiteralAndInsideAnObject_ConvertsToZeroAndFalse()
        => AssertPrintsVbcsAnswer(ObjectConversionProbes.O15, ObjectConversionProbes.N08);

    /// <summary>(7) `CType(o, Integer / Long / Double / Boolean)` of an Object is the same conversion as the intrinsic it lowers to: a boxed True is -1, a boxed "0" is False and 0.</summary>
    [Test]
    public void CType_OfAnObject_ToIntegerLongDoubleAndBoolean_IsTheSameConversion()
        => AssertPrintsVbcsAnswer(ObjectConversionProbes.O10);

    /// <summary>(8) CStr of an Object STAYS on Convert.ToString: `Len(CStr(n))` of a Nothing is 0 and its `&amp;` is "[]", not a NullReferenceException. ⛔ M5 (CStr adopts VB's Conversions.ToString) fails it on C#.</summary>
    [Test]
    public void CStr_OfAnObject_StaysOnConvert_SoLenOfANothingDoesNotThrow()
        => AssertPrintsVbcsAnswer(ObjectConversionProbes.CS1);

    /// <summary>(9) The ordinary case: a boxed Integer 7 through every conversion answers what it always did.</summary>
    [Test]
    public void AnOrdinaryBoxedInteger_StillConvertsEveryWay()
        => AssertPrintsVbcsAnswer(ObjectConversionProbes.O09);

    /// <summary>(10) The operand is a FUNCTION RESULT typed Object, not a literal-initialised local: a boxed True and "0", and a function that returns Nothing on one path and "1" on the other.</summary>
    [Test]
    public void AnObjectReturnedByAFunction_ConvertsLikeAnObjectLocal()
        => AssertPrintsVbcsAnswer(ObjectConversionProbes.O12, ObjectConversionProbes.O16);
}

/// <summary>
/// #212 SHAPE — the fast half. Nothing spawns: the C# and the IL the compiler writes, through the standard passes, the aggressive ones and <c>CompileProjectFiles</c>. The whole table of targets, the Nothing literal's own
/// rule, and the CONTROL that a typed source calls nothing in the VB runtime.
/// </summary>
[TestFixture]
public class ObjectConversionIntrinsicShapeTests
{
    private const string VbConversions = "Microsoft.VisualBasic.CompilerServices.Conversions";
    private const string VbConversionsIl = "[Microsoft.VisualBasic.Core]" + VbConversions;
    private const string VbRuntimeExtern = ".assembly extern Microsoft.VisualBasic.Core";

    private static IEnumerable<(string Name, string Text)> EmitsCSharp(string source)
    {
        yield return ("standard", ReturnCoercionTests.EmitCSharpForTest(source).Replace("\r\n", "\n"));
        yield return ("aggressive", ReturnCoercionTests.EmitCSharpAggressiveForTest(source).Replace("\r\n", "\n"));
        yield return ("project", TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, source).Replace("\r\n", "\n"));
    }

    private static IEnumerable<(string Name, string Text)> EmitsMsil(string source)
    {
        yield return ("plain", MsilHarness.CompileToIl(source, optimize: false).Replace("\r\n", "\n"));
        yield return ("standard", MsilHarness.CompileToIl(source, optimize: true).Replace("\r\n", "\n"));
        yield return ("aggressive", MsilHarness.CompileToIl(source, optimize: true, aggressive: true).Replace("\r\n", "\n"));
        yield return ("project", TempExec.Emit(Bk.Msil, EntryPoint.ProjectRelease, source).Replace("\r\n", "\n"));
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>
    /// (11) C#: an Object through each of the eleven targets is `Conversions.ToXxx(o)` — one call per intrinsic, each named for its own target — and CStr of it is still `Convert.ToString(o)`, with no `Conversions.ToString`
    /// and no other `Convert.To…`. M1 (the Object arm gone) leaves `Convert.ToInt32(o)` and fails every row.
    /// </summary>
    [Test]
    public void AnObjectConversion_OnCSharp_CallsEachVbConversionsMethod_AndCStrStaysConvert()
    {
        var failures = new List<string>();
        foreach (var (name, text) in EmitsCSharp(ObjectConversionProbes.EveryTarget))
        {
            foreach (var (intrinsic, method, _) in ObjectConversionProbes.Table)
                if (Count(text, $"{VbConversions}.{method}(o)") != 1)
                    failures.Add($"{name}: {intrinsic}(o) is not exactly one `{VbConversions}.{method}(o)` ({Count(text, $"{VbConversions}.{method}(o)")} found)");
            if (Count(text, "Convert.ToString(o)") != 1) failures.Add($"{name}: CStr(o) is not `Convert.ToString(o)`");
            if (text.Contains($"{VbConversions}.ToString")) failures.Add($"{name}: CStr(o) adopted VB's Conversions.ToString");
            // `Conversions.To…` does not contain "Convert.To" ("Conver" + "sions"), so the one match left is CStr's.
            if (Count(text, "Convert.To") != 1)
                failures.Add($"{name}: an Object conversion still goes through System.Convert ({Count(text, "Convert.To")} `Convert.To` found, only CStr's is expected):\n{text}");
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// (12) MSIL: an Object through each of the eleven targets is `call &lt;type&gt; [Microsoft.VisualBasic.Core]…Conversions::ToXxx(object)` — one per intrinsic — the module declares the VB runtime extern, and CStr of it is still
    /// `System.Convert::ToString(object)`, with no `Conversions::ToString`. M3 (the table never consulted) leaves System.Convert and fails every row; M4 (the extern dropped) fails the extern line.
    /// </summary>
    [Test]
    public void AnObjectConversion_OnMsil_CallsEachVbConversionsMethod_DeclaresTheExtern_AndCStrStaysConvert()
    {
        var failures = new List<string>();
        foreach (var (name, text) in EmitsMsil(ObjectConversionProbes.EveryTarget))
        {
            foreach (var (intrinsic, method, spec) in ObjectConversionProbes.Table)
            {
                var call = $"call {spec} {VbConversionsIl}::{method}(object)";
                if (Count(text, call) != 1) failures.Add($"{name}: {intrinsic}(o) is not exactly one `{call}` ({Count(text, call)} found)");
            }
            if (Count(text, "call string [mscorlib]System.Convert::ToString(object)") != 1) failures.Add($"{name}: CStr(o) is not `System.Convert::ToString(object)`");
            if (text.Contains("Conversions::ToString")) failures.Add($"{name}: CStr(o) adopted VB's Conversions::ToString");
            if (Count(text, "System.Convert::To") != 1) failures.Add($"{name}: an Object conversion still goes through System.Convert ({Count(text, "System.Convert::To")} found, only CStr's is expected)");
            if (!text.Contains(VbRuntimeExtern)) failures.Add($"{name}: the module calls the VB runtime and does not declare `{VbRuntimeExtern}`");
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// (13) The CONTROL. A typed Integer / Double source emits NO VB `Conversions` call, on either backend: a program with only typed conversions has none and, on MSIL, no `.assembly extern Microsoft.VisualBasic.Core`; next to
    /// ONE Object conversion (`CInt(o)`) it has exactly one `Conversions.ToInteger` / `Conversions::ToInteger` and nothing else of the kind, and the MSIL module declares the extern. Counting one (not "none") keeps it from passing
    /// on a backend that never sends an Object there.
    /// </summary>
    [Test]
    public void TypedConversions_EmitNoVbRuntimeCall_NextToExactlyOneForTheObjectOne()
    {
        var failures = new List<string>();

        foreach (var (name, text) in EmitsCSharp(ObjectConversionProbes.TypedOnly))
            if (text.Contains("Conversions.")) failures.Add($"C# {name}, typed only: a VB Conversions call:\n{text}");
        foreach (var (name, text) in EmitsMsil(ObjectConversionProbes.TypedOnly))
        {
            if (text.Contains("Conversions::")) failures.Add($"MSIL {name}, typed only: a VB Conversions call:\n{text}");
            if (text.Contains(VbRuntimeExtern)) failures.Add($"MSIL {name}, typed only: the module declares the VB runtime extern with nothing to call in it:\n{text}");
        }

        foreach (var (name, text) in EmitsCSharp(ObjectConversionProbes.TypedBesideOneObject))
            if (Count(text, "Conversions.") != 1 || Count(text, $"{VbConversions}.ToInteger(o)") != 1)
                failures.Add($"C# {name}, beside one Object: {Count(text, "Conversions.")} `Conversions.` calls where the one Object CInt is the only one:\n{text}");
        foreach (var (name, text) in EmitsMsil(ObjectConversionProbes.TypedBesideOneObject))
        {
            if (Count(text, "Conversions::") != 1 || Count(text, $"call int32 {VbConversionsIl}::ToInteger(object)") != 1)
                failures.Add($"MSIL {name}, beside one Object: {Count(text, "Conversions::")} `Conversions::` calls where the one Object CInt is the only one:\n{text}");
            if (!text.Contains(VbRuntimeExtern)) failures.Add($"MSIL {name}, beside one Object: the VB runtime extern is missing");
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// (14) M2, as TEXT. The Nothing LITERAL through CBool / CInt is `Conversions.ToBoolean((object)null)`: a bare `null` binds the `(string)` overload, whose `ToBoolean((string)null)` throws where CBool(Nothing) is False. The run
    /// case catches it too, but only in the Integration gate; this one is in the fast subset.
    /// </summary>
    [Test]
    public void TheNothingLiteral_OnCSharp_IsPassedAsAnObjectNull_NeverABareNull()
    {
        var failures = new List<string>();
        foreach (var (name, text) in EmitsCSharp(ObjectConversionProbes.NothingLiteral))
        {
            if (!text.Contains($"{VbConversions}.ToBoolean((object)null)")) failures.Add($"{name}: CBool(Nothing) is not `ToBoolean((object)null)`:\n{text}");
            if (!text.Contains($"{VbConversions}.ToInteger((object)null)")) failures.Add($"{name}: CInt(Nothing) is not `ToInteger((object)null)`:\n{text}");
            if (text.Contains(".ToBoolean(null)") || text.Contains(".ToInteger(null)")) failures.Add($"{name}: a bare `null` argument — it binds the (string) overload:\n{text}");
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }
}
