using NUnit.Framework;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// Task #183 — MSIL text pins for <c>&amp;</c> with a value operand and
/// <c>Console.Write</c>/<c>WriteLine</c> of every value type
/// (<c>BasicLang/MSILBackend.cs</c>: <c>EmitConcatOperandAsString</c>, <c>ValueTypeBoxToken</c>,
/// <c>ConsoleWriteOverload</c>, <c>TryEmitConsoleValueWrite</c>).
///
/// <para><b>Why IL text and not just a running program.</b> This fixture's job is the FIVE
/// mutants that change the emitted IL without changing any probe's printed output — measured in
/// <c>S/t183/mut/mut-results.txt</c> as "output-killed by NONE": (c) the small-int widen removed,
/// (d) the Single arm removed, (e) <c>box object</c> restored in the Console fallback, (f) the
/// Char path routed through <c>box</c>, and (g) the uint32/uint64 overloads removed. Each of
/// these falls back to boxing the value to its OWN type and calling <c>Object::ToString()</c> —
/// which happens to print the SAME text as the correct overload for every probe, so no running
/// program can tell the two apart. <see cref="MsilValueToStringExecutionTests"/> is the sibling
/// that runs the programs and catches the other nine mutants (a, b, b2, c2, d2, e2, f2, h, i, j),
/// several of which crash outright.</para>
///
/// <para><b>Every operand is sourced from a Function call</b>, never a literal assigned straight
/// into the <c>&amp;</c> or the <c>Console.Write</c>/<c>WriteLine</c> call — a literal String
/// gets folded by the front end even with the optimizer OFF (measured directly:
/// <c>"a" &amp; s</c> for <c>Dim s As String = "yo"</c> folds straight to <c>ldstr "ayo"</c>,
/// with no <c>Concat</c> call at all, before MSILBackend's value-to-string conversion is even
/// reached). This mirrors <see cref="MsilBinaryOperandCoercionTests"/>' own documented reason for
/// the same convention.</para>
///
/// <para><b>No <c>[Category("Integration")]</c></b>: <see cref="MsilHarness.CompileToIl"/> never
/// shells out to <c>ilasm</c> or spawns a process, so this fixture belongs in the fast subset.</para>
/// </summary>
[TestFixture]
public class MsilValueToStringTests
{
    /// <summary>Both pipelines this backend is measured on: the non-optimizing one and the
    /// standard optimizer (the CLI's <c>--optimize</c>/aggressive path is exercised by the
    /// execution fixture, which actually runs the Release <c>.blproj</c> build).</summary>
    private static void AssertOnBothPipelines(string source, System.Action<string> assertion)
    {
        assertion(CompileToIl(source, optimize: false));
        assertion(CompileToIl(source, optimize: true));
    }

    // ====================================================================================
    // Short, SByte, Byte, UShort — Console.WriteLine/Write must use (int32), no box.
    // Kills mutants (c) small-int widen removed and (c2) small-int raw spec.
    // ====================================================================================

    private static string SmallIntProgram(string vbType, string literal, string method) => $$"""
        Function V() As {{vbType}}
            Return {{literal}}
        End Function

        Sub Main()
            Console.{{method}}(V())
        End Sub
        """;

    [TestCase("Short", "3")]
    [TestCase("SByte", "-5")]
    [TestCase("Byte", "9")]
    [TestCase("UShort", "65000")]
    public void SmallIntegerTypes_WriteLine_UsesInt32OverloadWithNoBox(string vbType, string literal)
    {
        AssertOnBothPipelines(SmallIntProgram(vbType, literal, "WriteLine"), il =>
        {
            Assert.That(il, Does.Contain("call void [mscorlib]System.Console::WriteLine(int32)"),
                $"a {vbType} must call WriteLine(int32) — no such overload exists for its own " +
                $"raw spec (task #183, MissingMethodException).\n{il}");
            Assert.That(il, Does.Not.Contain("box "),
                $"a {vbType} must reach WriteLine(int32) directly — the load already " +
                $"sign/zero-extends it to int32, so no box is needed.\n{il}");
        });
    }

    [TestCase("Short", "3")]
    [TestCase("SByte", "-5")]
    [TestCase("Byte", "9")]
    [TestCase("UShort", "65000")]
    public void SmallIntegerTypes_Write_UsesInt32OverloadWithNoBox(string vbType, string literal)
    {
        AssertOnBothPipelines(SmallIntProgram(vbType, literal, "Write"), il =>
        {
            Assert.That(il, Does.Contain("call void [mscorlib]System.Console::Write(int32)"),
                $"a {vbType} must call Write(int32) — Write(int16)/(int8)/(uint16) does not " +
                $"exist (task #183, MissingMethodException).\n{il}");
            Assert.That(il, Does.Not.Contain("box "), $"no box expected for a {vbType}.\n{il}");
        });
    }

    // ====================================================================================
    // Single — must call (float32), never widen to (float64) (which printed 0.10000000149011612
    // where C# prints 0.1). Kills mutants (d) Single arm removed and (d2) widened to float64.
    // ====================================================================================

    private static string SingleProgram(string method) => $$"""
        Function V() As Single
            Dim r As Single = 1.5
            Return r
        End Function

        Sub Main()
            Console.{{method}}(V())
        End Sub
        """;

    [Test]
    public void Single_WriteLine_UsesFloat32NeverFloat64()
    {
        AssertOnBothPipelines(SingleProgram("WriteLine"), il =>
        {
            Assert.That(il, Does.Contain("call void [mscorlib]System.Console::WriteLine(float32)"));
            Assert.That(il, Does.Not.Contain("WriteLine(float64)"));
            Assert.That(il, Does.Not.Contain("box "), $"a Single needs no box.\n{il}");
        });
    }

    [Test]
    public void Single_Write_UsesFloat32NeverFloat64()
    {
        AssertOnBothPipelines(SingleProgram("Write"), il =>
        {
            Assert.That(il, Does.Contain("call void [mscorlib]System.Console::Write(float32)"));
            Assert.That(il, Does.Not.Contain("Write(float64)"));
        });
    }

    // ====================================================================================
    // UInteger, ULong — Console.WriteLine must use their own (uint32)/(uint64) overload, not
    // fall back to boxing. Kills mutant (g).
    // ====================================================================================

    [TestCase("UInteger", "4000000000", "uint32")]
    [TestCase("ULong", "9000000000", "uint64")]
    public void UnsignedWideTypes_WriteLine_UsesTheirOwnOverload(string vbType, string literal, string ilOverload)
    {
        var program = $$"""
            Function V() As {{vbType}}
                Return {{literal}}
            End Function

            Sub Main()
                Console.WriteLine(V())
            End Sub
            """;

        AssertOnBothPipelines(program, il =>
        {
            Assert.That(il, Does.Contain($"call void [mscorlib]System.Console::WriteLine({ilOverload})"),
                $"a {vbType} must call its own WriteLine({ilOverload}) overload, not fall back to " +
                $"boxing (task #183 mutant g).\n{il}");
            Assert.That(il, Does.Not.Contain("box "), $"no box expected for a {vbType}.\n{il}");
        });
    }

    // ====================================================================================
    // Char in `&` — Char::ToString(char), never boxed. Kills mutant (f).
    // ====================================================================================

    private const string CharConcat = """
        Function VCh() As Char
            Return "q"c
        End Function

        Sub Main()
            Console.WriteLine("c=" & VCh())
        End Sub
        """;

    [Test]
    public void Char_Concat_UsesCharToString_NeverBoxedChar()
    {
        AssertOnBothPipelines(CharConcat, il =>
        {
            Assert.That(il, Does.Contain("call string [mscorlib]System.Char::ToString(char)"),
                $"a Char operand of & must convert through Char::ToString(char) — task #171's " +
                $"existing byte-identical output.\n{il}");
            Assert.That(il, Does.Not.Contain("box [mscorlib]System.Char"),
                $"a Char must never be boxed for &  (task #183 mutant f).\n{il}");
        });
    }

    // ====================================================================================
    // Integer, Double, Boolean in `&`, on the LEFT and on the RIGHT of the operator — each must
    // box to its OWN type, call Object::ToString() virtually, before String::Concat(string,
    // string) — and never box [mscorlib]System.Object.
    // ====================================================================================

    private static string ConcatValueLeftProgram(string vbType, string literal) => $$"""
        Function V() As {{vbType}}
            Return {{literal}}
        End Function

        Sub Main()
            Console.WriteLine(V() & "!")
        End Sub
        """;

    private static string ConcatValueRightProgram(string vbType, string literal) => $$"""
        Function V() As {{vbType}}
            Return {{literal}}
        End Function

        Sub Main()
            Console.WriteLine("x=" & V())
        End Sub
        """;

    [TestCase("Integer", "5", "[mscorlib]System.Int32")]
    [TestCase("Double", "2.5", "[mscorlib]System.Double")]
    [TestCase("Boolean", "True", "[mscorlib]System.Boolean")]
    public void ValueOnTheLeftOfConcat_BoxesItsOwnType_ThenCallsObjectToString_BeforeConcat(
        string vbType, string literal, string boxToken)
    {
        AssertOnBothPipelines(ConcatValueLeftProgram(vbType, literal), il =>
        {
            Assert.That(il, Does.Contain($"box {boxToken}"));
            Assert.That(il, Does.Contain("callvirt instance string [mscorlib]System.Object::ToString()"));
            Assert.That(il, Does.Not.Contain("box [mscorlib]System.Object"),
                $"a {vbType} must box to its OWN type, never [mscorlib]System.Object.\n{il}");

            var boxIndex = il.IndexOf($"box {boxToken}", System.StringComparison.Ordinal);
            var concatIndex = il.IndexOf(
                "String::Concat(string, string)", System.StringComparison.Ordinal);
            Assert.That(boxIndex, Is.GreaterThanOrEqualTo(0).And.LessThan(concatIndex),
                $"the {vbType} conversion must happen before Concat is called.\n{il}");
        });
    }

    [TestCase("Integer", "5", "[mscorlib]System.Int32")]
    [TestCase("Double", "2.5", "[mscorlib]System.Double")]
    [TestCase("Boolean", "True", "[mscorlib]System.Boolean")]
    public void ValueOnTheRightOfConcat_BoxesItsOwnType_ThenCallsObjectToString_BeforeConcat(
        string vbType, string literal, string boxToken)
    {
        AssertOnBothPipelines(ConcatValueRightProgram(vbType, literal), il =>
        {
            Assert.That(il, Does.Contain($"box {boxToken}"));
            Assert.That(il, Does.Contain("callvirt instance string [mscorlib]System.Object::ToString()"));
            Assert.That(il, Does.Not.Contain("box [mscorlib]System.Object"),
                $"a {vbType} must box to its OWN type, never [mscorlib]System.Object.\n{il}");

            var boxIndex = il.IndexOf($"box {boxToken}", System.StringComparison.Ordinal);
            var concatIndex = il.IndexOf(
                "String::Concat(string, string)", System.StringComparison.Ordinal);
            Assert.That(boxIndex, Is.GreaterThanOrEqualTo(0).And.LessThan(concatIndex),
                $"the {vbType} conversion must happen before Concat is called.\n{il}");
        });
    }

    // ====================================================================================
    // The fallback for an enum or a Structure: WriteLine of each must box to its OWN type
    // (verified directly against this harness: an enum/Structure boxes to a plain quoted type
    // name, e.g. `box 'Shade'` / `box 'Pt'` — never [mscorlib]System.Object) and call
    // WriteLine(object). Kills mutants (e) box object restored, (e2) ValueTypeBoxToken -> object,
    // and (i) the enum/Structure kind test removed.
    //
    // ⛔ Assert the IL text only — NEITHER shape runs on MSIL today. #192: an MSIL enum local is
    // declared `class 'Shade'` where the type itself is a `valuetype`-shaped Enum, which is a
    // TypeLoadException at run time; a default (never-`New`'d) Structure local throws
    // NullReferenceException. Both are pre-existing gaps this task does not touch — see
    // MsilValueToStringExecutionTests' own note and docs/HANDOFF.md.
    // ====================================================================================

    private const string EnumWriteLine = """
        Enum Shade As Integer
            Red
            Green = 5
        End Enum

        Sub Main()
            Dim g As Shade = CType(5, Shade)
            Console.WriteLine(g)
        End Sub
        """;

    [Test]
    public void Enum_WriteLine_BoxesItsOwnType_NeverObject()
    {
        AssertOnBothPipelines(EnumWriteLine, il =>
        {
            Assert.That(il, Does.Contain("box 'Shade'"),
                $"an enum must box to its OWN type token before WriteLine(object) (task #183 " +
                $"mutants e/e2/i).\n{il}");
            Assert.That(il, Does.Contain("call void [mscorlib]System.Console::WriteLine(object)"));
            Assert.That(il, Does.Not.Contain("box [mscorlib]System.Object"),
                $"an enum must never box to [mscorlib]System.Object — that box is a no-op on a " +
                $"value and was the original #183 InvalidProgramException.\n{il}");
        });
    }

    private const string StructureWriteLine = """
        Structure Pt
            Public X As Integer
        End Structure

        Sub Main()
            Dim p As New Pt()
            p.X = 1
            Console.WriteLine(p)
        End Sub
        """;

    [Test]
    public void Structure_WriteLine_BoxesItsOwnType_NeverObject()
    {
        AssertOnBothPipelines(StructureWriteLine, il =>
        {
            Assert.That(il, Does.Contain("box 'Pt'"),
                $"a Structure must box to its OWN type token before WriteLine(object) (task #183 " +
                $"mutants e/e2/i).\n{il}");
            Assert.That(il, Does.Contain("call void [mscorlib]System.Console::WriteLine(object)"));
            Assert.That(il, Does.Not.Contain("box [mscorlib]System.Object"),
                $"a Structure must never box to [mscorlib]System.Object.\n{il}");
        });
    }

    // ====================================================================================
    // References are unchanged — the "byte-identical" half of the contract.
    // ====================================================================================

    private const string ReferenceProgram = """
        Function GetObj() As Object
            Return "hi"
        End Function

        Function GetStr() As String
            Return "yo"
        End Function

        Sub Main()
            Dim o As Object = GetObj()
            Console.WriteLine(o)
            Dim s As String = GetStr()
            Console.WriteLine(s)
            Console.WriteLine("a" & s)
        End Sub
        """;

    [Test]
    public void ObjectArgument_ToWriteLine_KeepsBoxObject_UnchangedFromBeforeTheFix()
    {
        // box object on a reference is a documented no-op (ECMA-335 III.4.1) and predates #183 —
        // this pins that the reference arm was NOT touched by the value-type fix. Line-ending
        // tolerant (Environment.NewLine differs Windows/Linux; this fixture never round-trips
        // through a file, so the raw generator text carries whichever the CI machine uses).
        AssertOnBothPipelines(ReferenceProgram, il =>
            Assert.That(il, Does.Match(
                @"box object\r?\n\s*call void \[mscorlib\]System\.Console::WriteLine\(object\)"),
                $"an Object argument must still box object then WriteLine(object), byte-identical " +
                $"to before this fix.\n{il}"));
    }

    [Test]
    public void StringArgument_ToWriteLine_UsesStringOverload_NoBox()
    {
        AssertOnBothPipelines(ReferenceProgram, il =>
            Assert.That(il, Does.Contain("call void [mscorlib]System.Console::WriteLine(string)")));
    }

    [Test]
    public void StringOperand_OfConcat_EmitsNoBoxAndNoToString()
    {
        // "a" & s for a String s: no box, no ToString call for the String operand — Concat takes
        // it as-is.
        AssertOnBothPipelines(ReferenceProgram, il =>
        {
            Assert.That(il, Does.Contain("ldstr \"a\""));
            Assert.That(il, Does.Contain("String::Concat(string, string)"));
            // Only ONE box in the whole program (Object -> object, from the WriteLine(o) line
            // above); the String concat itself must contribute no box/ToString of its own.
            var boxCount = System.Text.RegularExpressions.Regex.Matches(il, @"\bbox\b").Count;
            Assert.That(boxCount, Is.EqualTo(1),
                $"a String operand of & must add no box of its own.\n{il}");
        });
    }
}
