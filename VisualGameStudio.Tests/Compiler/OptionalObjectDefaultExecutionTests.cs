using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #216, RUN. C#: an Optional parameter typed Object with a non-Nothing default compiles.
//
//  ⛔ THE BUG. `Sub Show(Optional o As Object = 5)` was emitted as `object o = 5`, and C# allows only `null` as the default of a reference-typed parameter other than `string` (CS1763). `= "x"`, `= True`,
//  `= 2.5`, `= "a"c` and `= 5000000000L` failed the same way (a String into `object` is not an identity conversion either), through the CLI, the CLI with `--optimize` and a Release .blproj, for a module Sub, a class
//  method, a Shared method and an interface method with its implementation. MSIL and JavaScript already ran every one of them.
//  The fix is in `CSharpBackend`: `JoinParameters` is the ONE place C# spells a parameter default, and an Object Optional with a non-Nothing constant default is written the way vbc writes it,
//  `[System.Runtime.InteropServices.Optional, System.Runtime.InteropServices.DefaultParameterValue(5)] object o`, with no initializer. ⚠ C# accepts `= value` only on a SUFFIX of the list (CS1737) and an encoded
//  parameter counts as required, so every Optional BEFORE the last encoded one takes the encoding too (an `= value` Optional in front of an Object one was CS1737, measured); the ones after it keep `= value`.
//  The value is cast where the literal alone would give another type (CS1908 on a narrowing value; an uncast widening value is stored under the LITERAL's type), and a Decimal is `DecimalConstant` (CS0182).
//  ⚠ No BasicLang call needs the text: IRBuilder fills an omitted Optional at the CALL (#31), so every emitted call passes the value. The default has to compile and to say the right value, for reflection and for a
//  C# caller of the assembly. A list with no Object Optional that has a non-Nothing default is byte-identical to the old `= value` spelling.
//
//  ⭐ THE ORACLE IS vbc. Each probe's `Vb` is what the SDK's vbc prints for the program wrapped in a VB Module (S/t216/probes, the `.exp` beside each `.bas`, re-run through vbc by the test-writer: all eleven
//  matched). One probe needs a VB-legal TWIN: P11's class must say `Implements IShow.Show` on its method (vbc refuses the bare `Implements IShow`, BC30149); the twin prints the same three lines. The reflection
//  table in `OptionalObjectDefaultShapeTests` is vbc's own too: the same programs compiled by vbc, the parameter defaults read back with `ParameterInfo.DefaultValue` (value AND type).
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the IR optimizer"): every probe goes through the real CLI (standard passes), the real CLI with `--optimize` (aggressive)
//  and `BasicCompiler.CompileProjectFiles` with `OptimizeAggressive` (what a Release .blproj and the IDE call), on C#: `TempExec.Emit`. ⛔ Every C# RUN is HANG-SAFE (`CSharpProcessRunner`, a child process with a
//  time limit). The FAST half is `OptionalObjectDefaultShapeTests` below: what the metadata of the compiled program says, and the control that a list with no such parameter is spelled as before.
//
//  ⭐ MUTANTS (each is the fix plus ONE change, built from a plain source copy of the fix outside the worktree and run against a copy of the test output with its BasicLang.dll swapped; the cases that go red,
//  measured). Control: the PRE-FIX backend turns all thirteen cases that mention #216 red (CS1763): the eleven rows, the shape test and the moved pin `MsilObjectBoxingExecutionTests.E16_…`.
//    M1 only the Object parameter is encoded (no suffix rule)   -> the P09, P12 and P13 rows (CS1737: the `= value` Optional in front of the Object one) and the shape test. Four cases, nothing else.
//    M2 the value is never cast                                  -> the P13 row (CS1908: the Short's `-5` is a narrowing argument) and the shape test. Two cases. ⚠ P12's Long `= 7` and Double `= 7` are stored
//                                                                    as Int32 under M2 and only reflection sees it, but the compile error on P13 ends the shape test first.
//    M3 no DecimalConstant arm                                   -> the P13 row (CS0182: a decimal is not an attribute argument) and the shape test. Two cases.
//    M4 an Object parameter is cast to its own type, not the constant's -> the shape test ONLY (P15: the default is stored as Int32 5 where vbc stores Int64 5), and only because it reads the METADATA: no run can
//                                                                    see it, the call site writes the literal `5` whatever the default says (known gap below).
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect, or a defect that is another task's). Each is the same before and after #216:
//    * `Optional ByRef n As Integer = 5` is broken on EVERY backend (C# CS1741; MSIL and JavaScript refuse it; C++ finds no matching function). An Object ByRef Optional now fails on C# with CS1620 instead of CS1741,
//      because the filled call passes the default by value.
//    * `5L` boxed into an Object reads as Int32 on C#: the call site writes the literal `5`, and `Dim p As Object = 5L` does the same. The ENCODED default is right (Int64, asserted by reflection), so a C# caller
//      that omits the argument gets a Long and a BasicLang call that omits it gets an Int.
//    * A constructor's Optional parameters carry no default in C# metadata.
//    * A top-level Enum default (`Optional c As Color = Color.Red`) is refused by the front end.
//    * A Delegate or lambda Optional is refused: vbc refuses it too (BC33010) but with a different error.
//    * C++ has no Object at all ("Object has no C++ mapping"), so there is nothing to compile there.
//
//  ⚠ Named "…ExecutionTests" but it spawns no Node: it is C# only, so it is listed in JsExecutionTierRosterTests.NotJavaScriptExecution, and NOT in the roster.
// ================================================================================================

/// <summary>The #216 probe programs and vbc's answer for each. C# only; every run is hang-safe.</summary>
internal static class OptionalObjectDefaultProbes
{
    private static TempProbe P(string id, string source, string vb) => new(id, source, vb, Bk.CSharp, HangSafe: true);

    /// <summary>P01 — `= 5`, called without the argument, with a Double and with a String.</summary>
    internal static readonly TempProbe P01 = P("P01_integer", """
        Sub Show(Optional o As Object = 5)
            Console.WriteLine(o)
        End Sub
        Sub Main()
            Show()
            Show(3.5)
            Show("s")
        End Sub
        """, "5\n3.5\ns");

    /// <summary>P02 — `= "x"`: a String into Object is not an identity conversion either.</summary>
    internal static readonly TempProbe P02 = P("P02_string", """
        Sub Show(Optional o As Object = "x")
            Console.WriteLine(o)
        End Sub
        Sub Main()
            Show()
            Show(7)
        End Sub
        """, "x\n7");

    /// <summary>P04 — `= True`.</summary>
    internal static readonly TempProbe P04 = P("P04_boolean", """
        Sub Show(Optional o As Object = True)
            Console.WriteLine(o)
        End Sub
        Sub Main()
            Show()
            Show(False)
        End Sub
        """, "True\nFalse");

    /// <summary>P05 — `= 2.5`, then an Integer.</summary>
    internal static readonly TempProbe P05 = P("P05_double", """
        Sub Show(Optional o As Object = 2.5)
            Console.WriteLine(o)
        End Sub
        Sub Main()
            Show()
            Show(1)
        End Sub
        """, "2.5\n1");

    /// <summary>P10 — `= "a"c` and `= 5000000000L` (a Long that cannot be an Int32 by accident), each without its argument; the Char one with an Integer too.</summary>
    internal static readonly TempProbe P10 = P("P10_char_and_long", """
        Sub ShowC(Optional o As Object = "a"c)
            Console.WriteLine(o)
        End Sub
        Sub ShowL(Optional o As Object = 5000000000L)
            Console.WriteLine(o)
        End Sub
        Sub Main()
            ShowC()
            ShowL()
            ShowC(1)
        End Sub
        """, "a\n5000000000\n1");

    /// <summary>P06 — the same on a class method (an instance call) and a Shared method (a negative default).</summary>
    internal static readonly TempProbe P06 = P("P06_class_and_shared_method", """
        Class Box
            Public Function Tag(Optional o As Object = 42) As String
                Return "tag:" & o.ToString()
            End Function
            Public Shared Sub Note(Optional o As Object = -3)
                Console.WriteLine(o)
            End Sub
        End Class
        Sub Main()
            Dim b As New Box()
            Console.WriteLine(b.Tag())
            Console.WriteLine(b.Tag("z"))
            Box.Note()
            Box.Note(9)
        End Sub
        """, "tag:42\ntag:z\n-3\n9");

    /// <summary>P11 — an interface method and its implementation, called through the interface and through the class. (vbc's twin says `Implements IShow.Show` on the method; see the header.)</summary>
    internal static readonly TempProbe P11 = P("P11_interface_method", """
        Interface IShow
            Sub Show(Optional o As Object = 5)
        End Interface
        Class Shower
            Implements IShow
            Public Sub Show(Optional o As Object = 5)
                Console.WriteLine(o)
            End Sub
        End Class
        Sub Main()
            Dim s As IShow = New Shower()
            s.Show()
            s.Show(4)
            Dim t As New Shower()
            t.Show()
        End Sub
        """, "5\n4\n5");

    /// <summary>P09 — an `= value` Optional BEFORE the Object one, and one after it. ⛔ M1: `n` stays `= 1` and the program is CS1737.</summary>
    internal static readonly TempProbe P09 = P("P09_integer_before_object", """
        Sub Show(Optional n As Integer = 1, Optional o As Object = 5, Optional s As String = "s")
            Console.WriteLine(n & " " & o.ToString() & " " & s)
        End Sub
        Sub Main()
            Show()
            Show(2)
            Show(2, "o")
            Show(2, 6, "t")
        End Sub
        """, "1 5 s\n2 5 s\n2 o s\n2 6 t");

    /// <summary>P12 — a Char, a Single and a Long BEFORE the Object one. ⛔ M1 (CS1737 on the Char). M2: the Long's `= 7` is stored as an Int32 (reflection only).</summary>
    internal static readonly TempProbe P12 = P("P12_char_single_long_before_object", """
        Sub Show(Optional c As Char = "q"c, Optional f As Single = 1.5F, Optional l As Long = 7, Optional o As Object = 5)
            Console.WriteLine(c & " " & f & " " & l & " " & o.ToString())
        End Sub
        Sub Main()
            Show()
            Show("r"c, 2.5F, 8, "x")
        End Sub
        """, "q 1.5 7 5\nr 2.5 8 x");

    /// <summary>
    /// P13 — a Decimal, a Double given an integer, and a Short given a NEGATIVE integer before the Object one, and a String Nothing after it. ⛔ M2: the Short's `-5` is a narrowing argument (CS1908). M3: a decimal is not
    /// a valid attribute argument (CS0182). M1: CS1737.
    /// </summary>
    internal static readonly TempProbe P13 = P("P13_decimal_double_short_before_object", """
        Sub Show(Optional m As Decimal = 2, Optional d As Double = 7, Optional h As Short = -5, Optional o As Object = "z", Optional t As String = Nothing)
            Console.WriteLine(m & " " & d & " " & h & " " & o.ToString() & " " & (t Is Nothing))
        End Sub
        Sub Main()
            Show()
            Show(1, 2, 3, "o", "t")
        End Sub
        """, "2 7 -5 z True\n1 2 3 o False");

    /// <summary>P14 — the Object one FIRST, then an Integer and a String with `= value`, which must keep their initializers (the shape test reads them off the text).</summary>
    internal static readonly TempProbe P14 = P("P14_object_first", """
        Sub Show(Optional o As Object = True, Optional n As Integer = 1, Optional s As String = "s")
            Console.WriteLine(o.ToString() & " " & n & " " & s)
        End Sub
        Sub Main()
            Show()
            Show(False, 2)
        End Sub
        """, "True 1 s\nFalse 2 s");

    /// <summary>The 11 execution rows: each literal kind, then the class / Shared / interface shapes, then the mixed lists.</summary>
    internal static readonly TempProbe[] All = { P01, P02, P04, P05, P10, P06, P11, P09, P12, P13, P14 };

    /// <summary>P15 — an Object given `5L` and a Short after it: reflection only (the call site's `5L` reads as Int32 on C#, a known gap, so it is never RUN). ⛔ M4: the default is stored as an Int32.</summary>
    internal const string P15 = """
        Sub ShowL(Optional o As Object = 5L, Optional s As Short = 3)
            Console.WriteLine(o)
        End Sub
        Sub Main()
            ShowL()
        End Sub
        """;

    /// <summary>The control: no Object Optional with a non-Nothing default, on a module Sub (the function path) and on an interface method (the IR-parameter path), with a Nothing Object default after two initializers.</summary>
    internal const string Control = """
        Interface IShow
            Sub Show(Optional n As Integer = 5, Optional s As String = "x")
        End Interface
        Class Shower
            Implements IShow
            Public Sub Show(Optional n As Integer = 5, Optional s As String = "x")
                Console.WriteLine(n & s)
            End Sub
        End Class
        Sub Tail(Optional n As Integer = 5, Optional s As String = "x", Optional o As Object = Nothing)
            Console.WriteLine(n & s)
        End Sub
        Sub Main()
            Dim t As New Shower()
            t.Show()
            Tail()
        End Sub
        """;
}

/// <summary>#216 — an Object Optional with a non-Nothing default compiles on C# and prints vbc's answer, through every entry point.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the CLI legs spawn dotnet and the runner spawns children; keep the machine to this fixture
public class OptionalObjectDefaultExecutionTests
{
    public static IEnumerable<TempProbe> Programs => OptionalObjectDefaultProbes.All;

    /// <summary>The message of a failed assertion without the emitted program (which can be long): the first lines, up to the "--- emitted ---" marker. A compile failure keeps its Roslyn diagnostic (CS1763, CS1737).</summary>
    private static string Brief(string message)
        => string.Join(" // ", message.Replace("\r\n", "\n").Split('\n').TakeWhile(l => !l.StartsWith("--- emitted")).Take(4));

    /// <summary>
    /// Each probe on C#, through the CLI, the CLI with <c>--optimize</c> and <c>CompileProjectFiles</c>: each must print vbc's answer. A failure in one entry point is collected, not thrown, so the other two still
    /// report and the message names the entry point.
    /// </summary>
    [TestCaseSource(nameof(Programs))]
    public void AnObjectOptionalWithADefault_CompilesAndPrintsVbcsAnswer_OnCSharp(TempProbe probe)
    {
        var failures = new List<string>();
        foreach (var entry in Enum.GetValues<EntryPoint>())
        {
            try
            {
                var got = TempExec.Norm(TempExec.Run(Bk.CSharp, entry, probe.Source, hangSafe: true));
                if (got != TempExec.Norm(probe.Vb))
                    failures.Add($"{probe.Id} {entry}: printed [{got.Replace("\n", " | ")}] where vbc prints [{TempExec.Norm(probe.Vb).Replace("\n", " | ")}]");
            }
            catch (AssertionException ex)
            {
                failures.Add($"{probe.Id} {entry}: {Brief(ex.Message)}");
            }
        }

        Assert.That(failures, Is.Empty, $"on C#, through {string.Join(", ", Enum.GetValues<EntryPoint>())}:\n" + string.Join("\n", failures));
    }
}

/// <summary>
/// #216 SHAPE — the fast half. No process is spawned and no program is run: the emitted C# is compiled in memory and the parameter defaults are read back by reflection, and compared with what vbc stores for the same
/// program (value AND type). The text of a list with no such parameter is the CONTROL. Through the standard passes, the aggressive ones and <c>CompileProjectFiles</c>; the CLI legs are the fixture above.
/// </summary>
[TestFixture]
public class OptionalObjectDefaultShapeTests
{
    private static IEnumerable<(string Name, string Text)> Emits(string source)
    {
        yield return ("standard", ReturnCoercionTests.EmitCSharpForTest(source).Replace("\r\n", "\n"));
        yield return ("aggressive", ReturnCoercionTests.EmitCSharpAggressiveForTest(source).Replace("\r\n", "\n"));
        yield return ("project", TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, source).Replace("\r\n", "\n"));
    }

    /// <summary>One method whose parameter defaults vbc stores as <paramref name="Defaults"/>: the declaring type (null = any type with that method) and one (name, value) per parameter, a null value being a stored null.</summary>
    private sealed record Metadata(string Id, string Source, string? DeclaringType, string Method, (string Name, object? Value)[] Defaults);

    /// <summary>
    /// ⭐ vbc's own answers: each program wrapped in a VB Module, built with the SDK's vbc, and every parameter's <c>DefaultValue</c> read back (S/t216/tw/refl). Note the Object parameters: `= 5` is an Int32, `= "z"` a String,
    /// `= True` a Boolean and `= 5L` an Int64, and a Decimal / Single / Char / Int16 / Int64 keep their own type.
    /// </summary>
    private static readonly Metadata[] VbcMetadata =
    {
        new("P09", OptionalObjectDefaultProbes.P09.Source, null, "Show", new (string, object?)[] { ("n", 1), ("o", 5), ("s", "s") }),
        new("P12", OptionalObjectDefaultProbes.P12.Source, null, "Show", new (string, object?)[] { ("c", 'q'), ("f", 1.5f), ("l", 7L), ("o", 5) }),
        new("P13", OptionalObjectDefaultProbes.P13.Source, null, "Show", new (string, object?)[] { ("m", 2m), ("d", 7.0), ("h", (short)-5), ("o", "z"), ("t", null) }),
        new("P14", OptionalObjectDefaultProbes.P14.Source, null, "Show", new (string, object?)[] { ("o", true), ("n", 1), ("s", "s") }),
        new("P15", OptionalObjectDefaultProbes.P15, null, "ShowL", new (string, object?)[] { ("o", 5L), ("s", (short)3) }),
        new("P11 interface", OptionalObjectDefaultProbes.P11.Source, "IShow", "Show", new (string, object?)[] { ("o", 5) }),
        new("P11 class", OptionalObjectDefaultProbes.P11.Source, "Shower", "Show", new (string, object?)[] { ("o", 5) }),
    };

    /// <summary>The emitted C# compiled in memory into a collectible load context, <paramref name="read"/> called on it, then unloaded. Nothing in the program is run.</summary>
    private static T Reflect<T>(string csharp, Func<Assembly, T> read)
    {
        var bytes = FourBackends.CompileEmittedCSharp(csharp);
        var context = new AssemblyLoadContext("OptionalObjectDefault_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try { return read(context.LoadFromStream(new MemoryStream(bytes))); }
        finally { context.Unload(); }
    }

    private static string Describe(object? value) => value == null ? "null" : $"{value.GetType().Name}:{value}";

    /// <summary>The parameter defaults of the method <paramref name="m"/> names, or why they could not be read.</summary>
    private static List<string> Differences(Metadata m, string emitterName, string csharp)
        => Reflect(csharp, assembly =>
        {
            var found = assembly.GetTypes()
                .Where(t => m.DeclaringType == null || t.Name == m.DeclaringType)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(x => x.Name == m.Method)
                .ToList();
            if (found.Count != 1)
                return new List<string> { $"{m.Id} {emitterName}: {found.Count} methods named {m.Method} where there must be one:\n{csharp}" };

            var parameters = found[0].GetParameters();
            if (parameters.Length != m.Defaults.Length)
                return new List<string> { $"{m.Id} {emitterName}: {parameters.Length} parameters where vbc's has {m.Defaults.Length}" };

            var differences = new List<string>();
            for (var i = 0; i < parameters.Length; i++)
            {
                var (name, expected) = m.Defaults[i];
                var got = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : DBNull.Value;
                var same = got is DBNull
                    ? false
                    : expected == null ? got == null : got != null && got.GetType() == expected.GetType() && got.Equals(expected);
                if (!same)
                    differences.Add($"{m.Id} {emitterName} parameter {i + 1} ({name}): the default is [{(got is DBNull ? "none" : Describe(got))}] where vbc stores [{Describe(expected)}]");
            }

            return differences;
        });

    /// <summary>
    /// The metadata. Every parameter default in the compiled program is vbc's, value AND type: an Object's `= 5` an Int32, its `= 5L` an Int64 (M4: the Int32 the literal alone gives), a Long's `= 7` an Int64 and a Short's
    /// `-5` an Int16 (M2: uncast), a Decimal a Decimal (M3), a Char, a Single, a String and a stored Nothing. The signature text keeps `= value` on the parameters AFTER the last encoded one (P09's String, P14's Integer and
    /// String) and has no initializer on an encoded one. And the CONTROL: a list with no Object Optional that has a non-Nothing default (a module Sub and an interface method) is the old `type name = value` spelling,
    /// with no <c>Optional</c> and no <c>DefaultParameterValue</c> in the file, and an Object Nothing default is still `= null`.
    /// </summary>
    [Test]
    public void TheParameterDefaultsInTheMetadataAreVbcs_AndAListWithNoObjectOptionalIsSpelledAsBefore()
    {
        var failures = new List<string>();

        foreach (var m in VbcMetadata)
            foreach (var (name, text) in Emits(m.Source))
                failures.AddRange(Differences(m, name, text));

        const string encoding = "System.Runtime.InteropServices.Optional, System.Runtime.InteropServices.DefaultParameterValue";
        foreach (var (name, text) in Emits(OptionalObjectDefaultProbes.P14.Source))
            if (!text.Contains($"{encoding}(true)] object o, int n = 1, string s = \"s\")"))
                failures.Add($"P14 {name}: the Object parameter is not the encoded one followed by `int n = 1, string s = \"s\"`:\n{text}");
        foreach (var (name, text) in Emits(OptionalObjectDefaultProbes.P09.Source))
            if (!text.Contains($"{encoding}(5)] object o, string s = \"s\")"))
                failures.Add($"P09 {name}: the Object parameter is not the encoded one followed by `string s = \"s\"`:\n{text}");

        foreach (var (name, text) in Emits(OptionalObjectDefaultProbes.Control))
        {
            foreach (var spelled in new[]
                     {
                         "        void Show(int n = 5, string s = \"x\");", // the interface method
                         "public void Show(int n = 5, string s = \"x\")\n", // its implementation
                         "public static void Tail(int n = 5, string s = \"x\", object o = null)\n", // the module Sub
                     })
                if (!text.Contains(spelled))
                    failures.Add($"control {name}: `{spelled}` is not in the emitted C#:\n{text}");
            foreach (var forbidden in new[] { "Optional", "DefaultParameterValue", "DecimalConstant" })
                if (text.Contains(forbidden))
                    failures.Add($"control {name}: `{forbidden}` is in the emitted C# of a list with no Object Optional that has a non-Nothing default:\n{text}");
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }
}
