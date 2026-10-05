using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #191 — on MSIL, a class / interface / array reference through `&` and through Console.Write. RUN, through every entry point. MSIL only.
//
//  ⛔ THE BUG. `"k=" & obj` with a class operand reached `String.Concat(string, string)` RAW, as if the object were a string: it printed `k=` and whatever the object header read as string storage
//  (`k=` then garbage, or nothing). And `Console.Write(obj)` spelled the argument's own type, `Write(Foo)` — an overload that does not exist — so ilasm refused the program (`WriteLine(obj)` already
//  bound `WriteLine(object)` and was right). Now a reference operand of `&` takes `Convert.ToString(object)` (`o?.ToString() ?? ""`, which is C#'s `"k=" + obj`) and `Console.Write` of a class, an interface,
//  a delegate or an array binds `Write(object)` (`Write(char[])` for a one-dimensional Char array). Both ask ONE predicate, `MSILCodeGenerator.IsNonStringReference`: an array, or a class (not a
//  Structure), interface or delegate the PROGRAM declares — never `TypeKind.Class`, which is also what a .NET type the program merely names (Decimal, DateTime) is.
//
//  ⭐ TWO ORACLES, AND THE TEST SAYS WHICH. Not every row here is VB.
//    VB      `Console.Write(obj)` / `WriteLine(obj)` of a class, an interface, an array or a Structure are VALID VB, and vbc's answer is the oracle (S/t191/tw/probes/w1..w5, run with vbc).
//    PARITY  `"k=" & obj` with a class, an interface or an array operand is NOT valid VB: vbc REFUSES it (BC30452, "Operator '&' is not defined for types 'String' and 'Foo'") — measured on every
//            parity probe (S/t191/tw/probes/p1..p3). So those rows assert backend PARITY: MSIL prints what the C# backend prints (`obj.ToString()`, Nothing as ""), and each row also RUNS the C# backend
//            and requires the same text, so the claim cannot rot. They pin today's behaviour, NOT a VB rule: whether a typed class operand of `&` should be refused as VB refuses it, or stay accepted,
//            is the OWNER'S call — task #281. When it is decided the parity rows change with it (a refusal moves them to the refused-program tests; "stay accepted" keeps them).
//
//  Each row runs through the spawned CLI (standard passes), the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` with the aggressive passes (`TempExec.Emit`), then assembles with ilasm and
//  runs. A machine with no ilasm SKIPS the row (`TempExec.RequireTool`), never fails it. The text-only half of the contract (the IL a reference takes, with no ilasm) is `MsilObjectConcatAndWriteTextTests`.
//
//  ⭐ MUTANTS (S/t191/mut: the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped; each is killed by a committed test):
//    M1 `&` never converts a reference (`if (false && IsNonStringReference(...))`):  `AClassAnInterfaceAndAnArray_ThroughAmpersand_...` (concat garbage, as before the fix) and the fast `...ConvertThroughObjectToString`
//    M2 Console.Write never binds (object) / (char[]) (`if (true || !IsNonStringReference(type))`):  `WriteOfAClassAnInterfaceAnArrayAndAStructure_PrintsVbcsAnswer` (ilasm refuses `Write(Foo)`) and the fast `...AndWriteBindsObject` and `AOneDimensionalCharArray_...`
//    M3 the conversion is `callvirt Object::ToString()` instead of `Convert.ToString(object)` (a Nothing operand throws):  `ANothingClass_ThroughAmpersand_IsTheEmptyString_...` (NullReferenceException) and, as text, the fast `...ConvertThroughObjectToString`
//    M4 (extra) the FIRST-CUT predicate, `TypeKind` Class / Interface / Delegate / Array with no module lookup (a Decimal reads as a class):  the fast `ADecimalOperand_OfAmpersand_NeverReachesObjectToString` alone
//
//  ⛔ KNOWN GAPS — each NOT #191's, each measured on the fix commit, listed with NO test (asserting one would pin the defect):
//    - a default Structure local (`Dim p As Pt` with no `New`) throws NullReferenceException on MSIL, whatever it is passed to — task #192 (C9, C14). The Structure row here uses `New Pt()`.
//    - a Char array LOCAL does not assemble (`stind.u2` is a syntax error: the array's own spec is `class 'Char'[]`), so `Write(char[])` is pinned as IL TEXT only (C17).
//    - a generic class gives "Reference to undefined class 'T'" (C18) — task #239.
//    - a class with no `ToString` prints its type name, and that text differs per backend (MSIL `Foo`, as vbc; the C# backend `GeneratedCode.Foo`): the VB row uses one, no parity row does. C++ does not
//      build an `Overrides ToString` class here, and JavaScript prints `[object Object]` for a class operand. The fixture is MSIL-only on purpose.
//    - the #281 decision itself (see PARITY above).
//
//  ⚠ Named "…ExecutionTests" but NOT in JsExecutionTierRosterTests' roster: it runs no JavaScript, so it is in that file's `NotJavaScriptExecution`.
// ================================================================================================

/// <summary>
/// #191 RUN, MSIL only: `Console.Write` / `WriteLine` of a reference prints vbc's answer; `&amp;` with a class, interface, array or Nothing operand prints the C# backend's answer (PARITY, not VB — vbc
/// refuses it, BC30452; owner decision #281) — each through the CLI, the CLI with `--optimize` and CompileProjectFiles.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the spawned CLI, ilasm and the C# child run share the machine
public class MsilObjectConcatAndWriteExecutionTests
{
    // ================================================================================================
    // VB — Console.Write / WriteLine of a reference. vbc's answer. Kills M2.
    // ================================================================================================

    /// <summary>C4 + C5: a class with `Overrides ToString` through `WriteLine(object)` and `Write(object)` ("Write(Foo)" was the ilasm refusal).</summary>
    private const string W1_ClassWithToString = """
        Class Foo
            Public N As Integer
            Public Overrides Function ToString() As String
                Return "Foo#" & N
            End Function
        End Class
        Sub Main()
            Dim obj As New Foo()
            obj.N = 5
            Console.WriteLine(obj)
            Console.Write(obj)
            Console.WriteLine()
        End Sub
        """;

    /// <summary>C11: no `ToString` of its own, so the type name. vbc prints `Foo`, and so does MSIL (the C# backend says `GeneratedCode.Foo`, which is why no PARITY row uses it).</summary>
    private const string W2_ClassWithNoToString = """
        Class Foo
            Public N As Integer
        End Class
        Sub Main()
            Dim obj As New Foo()
            Console.WriteLine(obj)
            Console.Write(obj)
            Console.WriteLine()
        End Sub
        """;

    /// <summary>C10's two Console lines: an interface-typed local holding a class.</summary>
    private const string W3_Interface = """
        Interface IShape
            Function Area() As Integer
        End Interface
        Class Sq
            Implements IShape
            Public Function Area() As Integer
                Return 4
            End Function
            Public Overrides Function ToString() As String
                Return "Sq"
            End Function
        End Class
        Sub Main()
            Dim s As IShape = New Sq()
            Console.WriteLine(s)
            Console.Write(s)
            Console.WriteLine()
        End Sub
        """;

    /// <summary>C16's two Console lines: an Integer array binds `(object)` and prints its type name.</summary>
    private const string W4_IntegerArray = """
        Sub Main()
            Dim a(2) As Integer
            a(0) = 5
            Console.WriteLine(a)
            Console.Write(a)
            Console.WriteLine()
        End Sub
        """;

    /// <summary>C15's two Console lines: a Structure is NOT a reference. It boxes to its own type (#183) and must not be claimed by the reference predicate (`!cls.IsStruct`).</summary>
    private const string W5_Structure = """
        Structure Pt
            Public X As Integer
        End Structure
        Sub Main()
            Dim p As New Pt()
            p.X = 9
            Console.WriteLine(p)
            Console.Write(p)
            Console.WriteLine()
        End Sub
        """;

    // ================================================================================================
    // PARITY — `&` with a reference operand. NOT VB (vbc: BC30452); the C# backend's answer. Pending owner decision #281.
    // ================================================================================================

    /// <summary>C2 + C3 + C6: a class on either side of `&amp;`, in a chain, and into a String. Each operand is `o?.ToString() ?? ""`. Kills M1.</summary>
    private const string P1_ClassThroughAmpersand = """
        Class Foo
            Public N As Integer
            Public Overrides Function ToString() As String
                Return "Foo#" & N
            End Function
        End Class
        Sub Main()
            Dim obj As New Foo()
            obj.N = 7
            Console.WriteLine("k=" & obj)
            Console.WriteLine(obj & "x")
            Console.WriteLine("v=" & obj & "!")
            Dim s As String = "s=" & obj
            Console.WriteLine(s)
        End Sub
        """;

    /// <summary>C10 + C16: an interface-typed local and an Integer array through `&amp;` (an array prints its type name). Kills M1.</summary>
    private const string P2_InterfaceAndArrayThroughAmpersand = """
        Interface IShape
            Function Area() As Integer
        End Interface
        Class Sq
            Implements IShape
            Public Function Area() As Integer
                Return 4
            End Function
            Public Overrides Function ToString() As String
                Return "Sq"
            End Function
        End Class
        Sub Main()
            Dim s As IShape = New Sq()
            Console.WriteLine("s=" & s)
            Dim a(2) As Integer
            Console.WriteLine("a=" & a)
        End Sub
        """;

    /// <summary>C12: a class local that is Nothing is "" on either side of `&amp;` (not a NullReferenceException), and `Write(Nothing)` prints nothing. Kills M3.</summary>
    private const string P3_NothingClass = """
        Class Foo
            Public N As Integer
            Public Overrides Function ToString() As String
                Return "Foo#" & N
            End Function
        End Class
        Sub Main()
            Dim obj As Foo = Nothing
            Console.WriteLine("k=" & obj & "|")
            Console.WriteLine("[" & obj)
            Console.Write(obj)
            Console.WriteLine("end")
        End Sub
        """;

    // ================================================================================================
    // The runner
    // ================================================================================================

    /// <summary>Each program through the CLI (standard), the CLI with `--optimize` and CompileProjectFiles (aggressive): assemble with ilasm, run, and report every failing cell with its entry point.</summary>
    private static void AssertMsilInEveryEntryPoint((string Id, string Source, string Expected)[] probes, string oracle)
    {
        TempExec.RequireTool(Bk.Msil); // outside any multiple-assertion block: NUnit fails an Ignore inside one
        var failures = new List<string>();
        foreach (var (id, source, expected) in probes)
        {
            foreach (var entry in Enum.GetValues<EntryPoint>())
            {
                try
                {
                    var got = TempExec.Norm(TempExec.Run(Bk.Msil, entry, source));
                    if (got != TempExec.Norm(expected))
                        failures.Add($"{id}, {entry}: MSIL printed [{got.Replace("\n", " | ")}] where {oracle} prints [{TempExec.Norm(expected).Replace("\n", " | ")}]");
                }
                catch (AssertionException ex)
                {
                    failures.Add($"{id}, {entry}: {ex.Message.Split('\n')[0]}");
                }
            }
        }
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>The PARITY half: the C# backend, run through the CLI in a child process with a time limit (#256), prints the same text the MSIL rows expect. If it ever stops, the row's name is a lie.</summary>
    private static void AssertTheCSharpBackendPrintsTheSame((string Id, string Source, string Expected)[] probes)
    {
        var failures = new List<string>();
        foreach (var (id, source, expected) in probes)
        {
            try
            {
                var got = TempExec.Norm(TempExec.Run(Bk.CSharp, EntryPoint.Cli, source, hangSafe: true));
                if (got != TempExec.Norm(expected))
                    failures.Add($"{id}: the C# backend printed [{got.Replace("\n", " | ")}], not the [{TempExec.Norm(expected).Replace("\n", " | ")}] the MSIL parity row expects");
            }
            catch (AssertionException ex)
            {
                failures.Add($"{id}: the C# backend: {ex.Message.Split('\n')[0]}");
            }
        }
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// VB: `Console.Write` / `WriteLine` of a class with and without `ToString`, an interface, an Integer array and a Structure print vbc's answer. Before the fix `Write(obj)` named `Write(Foo)`, an overload
    /// that does not exist, and ilasm refused the program. Kills M2.
    /// </summary>
    [Test]
    public void WriteOfAClassAnInterfaceAnArrayAndAStructure_PrintsVbcsAnswer_ThroughEveryEntryPoint()
        => AssertMsilInEveryEntryPoint(new[]
        {
            ("w1_class_with_ToString", W1_ClassWithToString, "Foo#5\nFoo#5"),
            ("w2_class_no_ToString", W2_ClassWithNoToString, "Foo\nFoo"),
            ("w3_interface", W3_Interface, "Sq\nSq"),
            ("w4_Integer_array", W4_IntegerArray, "System.Int32[]\nSystem.Int32[]"),
            ("w5_Structure", W5_Structure, "Pt\nPt"),
        }, "vbc");

    /// <summary>
    /// PARITY, not VB (vbc refuses it: BC30452; owner decision #281): a class operand of `&amp;`, on either side, in a chain and into a String, prints `obj.ToString()` as the C# backend does — and so does an
    /// interface-typed local and an Integer array. Before the fix the operand reached `String.Concat(string, string)` raw: `k=` and garbage. Kills M1.
    /// </summary>
    [Test]
    public void AClassAnInterfaceAndAnArray_ThroughAmpersand_PrintTheCSharpBackendsAnswer_ParityNotVb_Pending281()
    {
        var probes = new[]
        {
            ("p1_class", P1_ClassThroughAmpersand, "k=Foo#7\nFoo#7x\nv=Foo#7!\ns=Foo#7"),
            ("p2_interface_and_array", P2_InterfaceAndArrayThroughAmpersand, "s=Sq\na=System.Int32[]"),
        };
        AssertMsilInEveryEntryPoint(probes, "the C# backend (parity, not VB)");
        AssertTheCSharpBackendPrintsTheSame(probes);
    }

    /// <summary>
    /// PARITY, not VB (vbc refuses `"k=" &amp; obj`: BC30452; owner decision #281): a class local that is Nothing is "" on either side of `&amp;` and `Write(Nothing)` prints nothing, as on the C# backend.
    /// `callvirt Object::ToString()` on it throws NullReferenceException. Kills M3.
    /// </summary>
    [Test]
    public void ANothingClass_ThroughAmpersand_IsTheEmptyString_ParityNotVb_Pending281()
    {
        var probes = new[] { ("p3_nothing_class", P3_NothingClass, "k=|\n[\nend") };
        AssertMsilInEveryEntryPoint(probes, "the C# backend (parity, not VB)");
        AssertTheCSharpBackendPrintsTheSame(probes);
    }
}

/// <summary>
/// #191 TEXT, MSIL only, in process: no ilasm, no spawned process, so it runs in the fast subset. What a reference takes through `&amp;` and `Console.Write`, read off the generated IL on both pipelines
/// (non-optimizing and the standard optimizer): `Convert.ToString(object)` and `Write(object)` for a class, an interface and an array; `Write(char[])` for a one-dimensional Char array (its program does not
/// assemble — a Char array local is a known gap — so the text is all there is to pin); and a Decimal operand, which is NOT a reference, never sent through `Convert.ToString(object)`.
/// </summary>
[TestFixture]
public class MsilObjectConcatAndWriteTextTests
{
    private static void OnBothPipelines(string source, Action<string> assertion)
    {
        assertion(MsilHarness.CompileToIl(source, optimize: false));
        assertion(MsilHarness.CompileToIl(source, optimize: true));
    }

    private const string ObjectToString = "call string [mscorlib]System.Convert::ToString(object)";

    /// <summary>
    /// A class, an interface, an Integer array and a user delegate: `"k=" &amp; r` converts through `Convert.ToString(object)` and `Console.Write(r)` binds `Write(object)` — never the raw `Write(Foo)` / `Write(IShape)` /
    /// `Write(Integer[])`, none of which exists. A Structure beside them is NOT converted that way. Kills M1 (no conversion) and M2 (the raw spelling) without ilasm.
    /// </summary>
    [Test]
    public void AClassAnInterfaceAndAnArray_ConvertThroughObjectToString_AndWriteBindsObject()
    {
        var cases = new (string Name, string Declarations, string Local, string Raw)[]
        {
            ("class", "Class Foo\n    Public N As Integer\nEnd Class\n", "Dim r As New Foo()", "Write(Foo)"),
            ("interface", "Interface IShape\n    Function Area() As Integer\nEnd Interface\nClass Sq\n    Implements IShape\n    Public Function Area() As Integer\n        Return 4\n    End Function\nEnd Class\n", "Dim r As IShape = New Sq()", "Write(IShape)"),
            ("array", "", "Dim r(2) As Integer", "Write(Integer[])"),
            ("delegate", "Delegate Function Op(x As Integer) As Integer\nFunction Twice(x As Integer) As Integer\n    Return x * 2\nEnd Function\n", "Dim r As Op = AddressOf Twice", "Write(Op)"),
        };
        Assert.Multiple(() =>
        {
            foreach (var (name, declarations, local, raw) in cases)
            {
                var source = declarations + "Sub Main()\n    " + local + "\n    Console.WriteLine(\"k=\" & r)\n    Console.Write(r)\n    Console.WriteLine()\nEnd Sub\n";
                OnBothPipelines(source, il =>
                {
                    Assert.That(il, Does.Contain(ObjectToString), $"{name}: `\"k=\" & r` must convert through Convert.ToString(object) (task #191: it reached Concat raw).\n{il}");
                    Assert.That(il, Does.Contain("call void [mscorlib]System.Console::Write(object)"), $"{name}: Console.Write(r) must bind Write(object).\n{il}");
                    Assert.That(il, Does.Not.Contain("Console::" + raw), $"{name}: Console.Write(r) must not name {raw}, an overload that does not exist.\n{il}");
                });
            }
        });
    }

    /// <summary>
    /// `Console.Write` of a ONE-dimensional Char array binds `Write(char[])` — it prints its characters, as VB and C# do — and never `Write(object)`, which would turn ilasm's refusal into a silent
    /// `System.Char[]`. An Integer array beside it still binds `Write(object)`: only a Char vector has an overload of its own. Text only: a Char array local does not assemble yet (`stind.u2`).
    /// </summary>
    [Test]
    public void AOneDimensionalCharArray_WriteBindsCharArray_NeverObject()
    {
        const string vector = "Sub Main()\n    Dim c(1) As Char\n    Console.Write(c)\nEnd Sub\n";
        const string integers = "Sub Main()\n    Dim a(1) As Integer\n    Console.Write(a)\nEnd Sub\n";
        Assert.Multiple(() =>
        {
            OnBothPipelines(vector, il =>
            {
                Assert.That(il, Does.Contain("call void [mscorlib]System.Console::Write(char[])"), $"a Char array must bind Write(char[]).\n{il}");
                Assert.That(il, Does.Not.Contain("Console::Write(object)"), $"a Char array must never bind Write(object): that prints System.Char[].\n{il}");
            });
            OnBothPipelines(integers, il =>
            {
                Assert.That(il, Does.Contain("call void [mscorlib]System.Console::Write(object)"), $"an Integer array has no Write overload of its own: it binds Write(object).\n{il}");
                Assert.That(il, Does.Not.Contain("Console::Write(char[])"), $"only a Char array binds Write(char[]).\n{il}");
            });
        });
    }

    /// <summary>
    /// ⛔ The regression the byte compare caught: a Decimal is a VALUE type the program merely names, which `TypeKind.Class` reads as a class. Taken as a reference, `"d=" &amp; m` reached
    /// `Convert.ToString(object)` with the decimal UNBOXED. A Decimal operand of `&amp;` never takes that call (it is boxed to its own type, then `Object.ToString()`), and a Structure beside it, likewise (#183), is never claimed as a reference.
    /// </summary>
    [Test]
    public void ADecimalOperand_OfAmpersand_NeverReachesObjectToString()
    {
        const string decimalSource = """
            Function V() As Decimal
                Dim r As Decimal = 1.5
                Return r
            End Function
            Sub Main()
                Dim m As Decimal = V()
                Console.WriteLine("d=" & m)
                Console.WriteLine(m & "!")
            End Sub
            """;
        const string structureSource = """
            Structure Pt
                Public X As Integer
            End Structure
            Sub Main()
                Dim p As New Pt()
                Console.WriteLine("p=" & p)
            End Sub
            """;
        Assert.Multiple(() =>
        {
            OnBothPipelines(decimalSource, il =>
            {
                Assert.That(il, Does.Not.Contain(ObjectToString), $"a Decimal operand of & must never be sent through Convert.ToString(object) (task #191: it arrived unboxed).\n{il}");
                Assert.That(Regex.IsMatch(il, @"box \[mscorlib\]System\.Decimal\s+callvirt instance string \[mscorlib\]System\.Object::ToString\(\)"), Is.True, $"a Decimal operand is boxed to its own type, then ToString.\n{il}");
            });
            OnBothPipelines(structureSource, il =>
            {
                Assert.That(il, Does.Not.Contain(ObjectToString), $"a Structure operand of & is not a reference: it must not be sent through Convert.ToString(object).\n{il}");
                Assert.That(Regex.IsMatch(il, @"box 'Pt'\s+callvirt instance string \[mscorlib\]System\.Object::ToString\(\)"), Is.True, $"a Structure operand is boxed to its own type (#183), then ToString.\n{il}");
            });
        });
    }
}
