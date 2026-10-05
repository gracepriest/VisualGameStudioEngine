using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Net;
using NUnit.Framework;
using VisualGameStudio.Tests.Blnet;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #194 — a .NET class WIDENS to its .NET base classes and to the interfaces it implements, the VB way (legal under Option Strict On). RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. `Dim s As Stream = New MemoryStream()`, `s = New MemoryStream()`, `F(New MemoryStream())` for a `Sub F(s As Stream)`, `Return New MemoryStream()` from a `Function … As Stream`,
//  `Dim e As IEnumerable(Of Integer) = New List(Of Integer)()`, `Dim c As ICollection = New ArrayList()`, `Dim d As IDisposable = New MemoryStream()` and `Dim ex As Exception = New ArgumentException("x")` were
//  refused everywhere ("Cannot assign value of type 'MemoryStream' to variable of type 'Stream'"): a type named for .NET is minted by the synthetic fallback of type resolution with no BaseType chain and no
//  interface list, so `TypeInfo.IsAssignableFrom` could only compare names. Now the four mint sites arm the type (`SemanticAnalyzer.WithNetWidening` -> `TypeInfo.NetWidensTo`) and `IsAssignableFrom`
//  consults it after its own rules refuse; `SemanticAnalyzer.NetWidens` spells both sides as .NET (`TryMapNetArgumentType`) and asks `NetTypeResolver.WidensByReference` — Roslyn's ClassifyConversion, an
//  implicit identity or reference conversion only. Narrowing (BC30512) and an unrelated pair (BC30311) stay refused, exactly as vbc refuses them.
//
//  ⭐ THE ORACLE IS vbc (Option Strict On). Each probe's `Vb` is what the SDK's vbc prints for the program wrapped in a VB Module (S/t194/probes, the `.exp` beside each `.bas`; the two-file program was
//  measured with vbc as one concatenated module) — never what a backend printed. A row runs each of its probes through THREE entry points and reports every failing cell by probe id: the spawned CLI
//  (standard passes), the CLI with `--optimize`, and `BasicCompiler.CompileProjectFiles` with the aggressive passes — what a Release .blproj build and the IDE's build service call. Every C# cell runs in a
//  child process with a time limit (`TempExec.Run(..., hangSafe: true)` -> `CSharpProcessRunner`), never in the in-process runner that has no timeout (#256).
//
//  ⛔ THE PROJECT LEG ARMS .NET RESOLUTION ITSELF. `TempExec.Emit`'s `ProjectRelease` builds `CompilerOptions` WITHOUT `EnableNetResolution()`, and with no resolver the fix is dormant by design (the LSP and a
//  WinForms/WPF project keep today's refusal). The CLI (`Program.cs`) and the IDE's `BuildService` both arm it, so a project leg that does not is testing a configuration nobody ships: the helper here
//  arms it. MEASURED: with the arming line removed every row here goes red on the `ProjectRelease` leg alone (the two CLI legs stay green), which reads as a product bug and is not one.
//
//  ⭐ THE TWO-FILE ROW is the IDE's route through ANOTHER file's signatures (`Function MakeMs() As MemoryStream` in Util.bas, called from Main.bas). A sibling still PENDING when a file is analyzed is
//  seen through signatures minted from its declarations (`ResolveSiblingSignatureType`, which carries its own copies of the mint sites); one that has COMPLETED is seen through its exports, whose types its
//  OWN analysis minted. Which route a file takes follows the compile order, so each order of the two files is compiled: MEASURED, M3 below fails the `Util.bas, Main.bas` order alone (Util completes first)
//  and the `Main.bas, Util.bas` order (Util pending) stays green. ⚠ No mutant here unstamps the pending-sibling copies alone.
//
//  ⛔ KNOWN GAPS — each NOT #194's (or held), each listed with NO test (asserting one would pin the defect):
//    MSIL   a widening program still fails at ilasm with "undefined class": a .NET class other than an exception or a collection is spelled as a class the module does not define. Only the Exception base (W09)
//           runs, and it is the one MSIL row here. Spelling them as resolver-backed `[mscorlib]` types is HELD (task #283) because it would turn those refusals into SILENT WRONG ANSWERS: `=` on
//           `System.Version` lowers to `ceq`, not `op_Equality` (prints False, vbc True); an Iterator returning `IEnumerable(Of Integer)` throws NullReferenceException; and .NET members are guessed.
//    FRONT  `Dim t As TextWriter = Console.Out` (W08) is still refused: `Console.Out` is typed Object by the member table, a separate gap.
//    FRONT  a generic argument that is a USER type (`IEnumerable(Of Player)` <- `List(Of Player)`), and covariance to `IEnumerable(Of Object)`, are still refused: the judge never asks about a user type.
//    C++    `Dim ex As Exception` and `Dim e As IEnumerable(Of Integer)` fail in clang whatever they are assigned — already so before this change, so no C++ row.
//    FRONT  with no resolver (the LSP, a WinForms/WPF project) today's refusal stands; the fast fixture below pins only the armed configuration.
//
//  ⭐ MUTANTS (S/t194/mut: the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped; each is killed by a committed test):
//    M1 the `IsAssignableFrom` arm dropped (the stamp is set, never consulted):  all seven execution rows here (the MSIL one included) and the fast `WideningIsAccepted_...`.
//    M2 `WidensByReference` admits any conversion that EXISTS (an explicit one included):  the fast `ANarrowing_IsRefused_AsVbcRefusesIt` (W10, MemoryStream <- Stream) and
//       `AnUnrelatedOrNarrowingPair_...` (its IEnumerable -> List row).
//    M3 the generic mint site unstamped (a `List(Of T)` source never widens to `IEnumerable(Of T)` / `ICollection(Of T)`):  `AGenericInterface_FromAGenericList_...` (W06 / W16), the two-file row (the
//       `Util.bas, Main.bas` order) and the fast `WideningIsAccepted_...` (W06).
//
//  ⚠ Named "…ExecutionTests" but NOT in JsExecutionTierRosterTests' roster: it is C# (and one MSIL row), no JavaScript, so it is in that file's `NotJavaScriptExecution`.
// ================================================================================================

/// <summary>One program and what vbc (Option Strict On) prints for it.</summary>
internal sealed record WideningProbe(string Id, string Source, string Vb)
{
    public override string ToString() => Id;
}

/// <summary>
/// #194 RUN: a .NET class widens to its base class and to its interfaces — each program prints vbc's answer on C# (and the Exception base also on MSIL), through the CLI, the CLI with `--optimize` and
/// `CompileProjectFiles`, plus the two-file sibling-signature build.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the spawned CLI, ilasm and the C# child runs share the machine
public class NetSubtypeWideningExecutionTests
{
    // ------------------------------------------------------------------------------------------------
    // The probes. Sources are the implementer's (S/t194/probes/W*.bas), expectations are vbc's (the .exp files).
    // ------------------------------------------------------------------------------------------------

    internal static readonly WideningProbe W01 = new("W01_Dim_Stream_from_MemoryStream", """
        Using System.IO
        Sub Main()
            Dim s As Stream = New MemoryStream()
            s.WriteByte(65)
            s.WriteByte(66)
            Console.WriteLine(s.Length)
        End Sub
        """, "2");

    internal static readonly WideningProbe W02 = new("W02_assignment_after_Dim", """
        Using System.IO
        Sub Main()
            Dim s As Stream
            s = New MemoryStream()
            s.WriteByte(1)
            s.WriteByte(2)
            s.WriteByte(3)
            Console.WriteLine(s.Length)
            Console.WriteLine(s.Position)
        End Sub
        """, "3\n3");

    internal static readonly WideningProbe W03 = new("W03_argument", """
        Using System.IO
        Sub F(s As Stream)
            s.WriteByte(1)
            Console.WriteLine(s.Length)
        End Sub
        Sub Main()
            Dim m As New MemoryStream()
            m.WriteByte(9)
            F(m)
            F(New MemoryStream())
        End Sub
        """, "2\n1");

    internal static readonly WideningProbe W04 = new("W04_Return", """
        Using System.IO
        Function Make() As Stream
            Return New MemoryStream()
        End Function
        Sub Main()
            Dim s As Stream = Make()
            s.WriteByte(7)
            Console.WriteLine(s.Length)
        End Sub
        """, "1");

    /// <summary>The widened value is still the same object: `TypeOf`, `Is`, an `Object` and an `IDisposable` view of it, and a `Show(Stream)` taking it three ways.</summary>
    internal static readonly WideningProbe W15 = new("W15_identity_survives_the_widening", """
        Using System.IO
        Sub Show(s As Stream)
            Console.WriteLine(s IsNot Nothing)
        End Sub
        Function Make() As Stream
            Return New MemoryStream()
        End Function
        Sub Main()
            Dim s As Stream = New MemoryStream()
            Show(s)
            Show(Make())
            Show(New MemoryStream())
            Console.WriteLine(TypeOf s Is MemoryStream)
            Dim o As Object = s
            Console.WriteLine(o Is s)
            Dim d As IDisposable = s
            Console.WriteLine(d Is s)
        End Sub
        """, "True\nTrue\nTrue\nTrue\nTrue\nTrue");

    /// <summary>A class to a generic interface, twice: a `New` and a variable. ⛔ Kills M3 with W16 — the generic mint site.</summary>
    internal static readonly WideningProbe W06 = new("W06_IEnumerable_Of_Integer_from_List_Of_Integer", """
        Sub Main()
            Dim e As IEnumerable(Of Integer) = New List(Of Integer)()
            Dim n As Integer = 0
            For Each x As Integer In e
                n = n + 1
            Next
            Console.WriteLine(n)
            Dim l As New List(Of Integer)()
            l.Add(3)
            l.Add(4)
            Dim e2 As IEnumerable(Of Integer) = l
            Dim t As Integer = 0
            For Each y As Integer In e2
                t = t + y
            Next
            Console.WriteLine(t)
        End Sub
        """, "0\n7");

    /// <summary>`IEnumerable(Of String)` and `ICollection(Of String)` from a `List(Of String)`, the second proven by identity. Kills M3 with W06.</summary>
    internal static readonly WideningProbe W16 = new("W16_two_generic_interfaces_from_List_Of_String", """
        Sub Main()
            Dim l As New List(Of String)()
            l.Add("a")
            l.Add("bc")
            Dim e As IEnumerable(Of String) = l
            Dim n As Integer = 0
            For Each x As String In e
                n = n + x.Length
            Next
            Console.WriteLine(n)
            Dim c As ICollection(Of String) = l
            Console.WriteLine(c Is l)
        End Sub
        """, "3\nTrue");

    internal static readonly WideningProbe W07 = new("W07_ICollection_from_ArrayList", """
        Using System.Collections
        Sub Main()
            Dim c As ICollection = New ArrayList()
            Console.WriteLine(c.Count)
            Dim a As New ArrayList()
            a.Add(1)
            a.Add(2)
            Dim c2 As ICollection = a
            Console.WriteLine(c2.Count)
        End Sub
        """, "0\n2");

    internal static readonly WideningProbe W13 = new("W13_IDisposable_from_MemoryStream", """
        Using System.IO
        Sub Main()
            Dim d As IDisposable = New MemoryStream()
            d.Dispose()
            Console.WriteLine("ok")
        End Sub
        """, "ok");

    /// <summary>An Exception base from a subclass. The one widening that also RUNS on MSIL (an exception is spelled as a class the module can name).</summary>
    internal static readonly WideningProbe W09 = new("W09_Exception_from_ArgumentException", """
        Sub Main()
            Dim ex As Exception = New ArgumentException("x")
            Console.WriteLine(ex.Message)
            Console.WriteLine(TypeOf ex Is ArgumentException)
        End Sub
        """, "x\nTrue");

    /// <summary>A `List(Of Stream)` holding a MemoryStream — the container the widened value goes into (the Add argument is never typed, so this ran before the fix; it is the control that the widening did not break it).</summary>
    internal static readonly WideningProbe W12 = new("W12_List_Of_Stream_holding_a_MemoryStream", """
        Using System.IO
        Sub Main()
            Dim l As New List(Of Stream)()
            l.Add(New MemoryStream())
            Dim ms As New MemoryStream()
            ms.WriteByte(1)
            ms.WriteByte(2)
            l.Add(ms)
            Console.WriteLine(l.Count)
            Console.WriteLine(l(1).Length)
        End Sub
        """, "2\n2");

    /// <summary>The way back DOWN stays legal when it is written: `CType(s, MemoryStream)` is an explicit cast, not the implicit narrowing W10 refuses.</summary>
    internal static readonly WideningProbe W11 = new("W11_CType_narrows_back_down", """
        Using System.IO
        Sub Main()
            Dim s As Stream = New MemoryStream()
            s.WriteByte(5)
            Dim m As MemoryStream = CType(s, MemoryStream)
            Console.WriteLine(m.Length)
        End Sub
        """, "1");

    // ------------------------------------------------------------------------------------------------
    // The entry points
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Generated text through <paramref name="entry"/>. The CLI legs are the shared harness; the project leg is <c>CompileProjectFiles</c> with .NET resolution ARMED, as the CLI and the IDE's BuildService
    /// arm it (<c>TempExec.Emit</c>'s own project leg does not — see the header). <paramref name="files"/> are written to a temp directory in the given order.
    /// </summary>
    private static string Emit(Bk backend, EntryPoint entry, params (string Name, string Text)[] files)
    {
        if (entry != EntryPoint.ProjectRelease)
        {
            Assert.That(files, Has.Length.EqualTo(1), "the CLI legs here take one file");
            return TempExec.Emit(backend, entry, files[0].Text);
        }

        var options = new CompilerOptions { OptimizeAggressive = true, TargetBackend = TempExec.TargetName(backend) };
        options.EnableNetResolution();
        var compiler = new BasicCompiler(options);

        var dir = Path.Combine(Path.GetTempPath(), "bl-t194-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = new List<string>();
            foreach (var (name, text) in files)
            {
                var path = Path.Combine(dir, name);
                File.WriteAllText(path, text);
                paths.Add(path);
            }
            var result = compiler.CompileProjectFiles(paths);
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the project entry point produced no combined IR");
            return backend switch
            {
                Bk.CSharp => new ImprovedCSharpCodeGenerator().Generate(result.CombinedIR),
                Bk.Msil => new MSILCodeGenerator().Generate(result.CombinedIR),
                _ => throw new ArgumentException(backend.ToString()),
            };
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>
    /// Every probe, every entry point, one backend: each must print vbc's answer. The tool check comes first and OUTSIDE any multiple-assertion block (NUnit fails an Ignore inside one). A failed compile or run
    /// in one cell is collected, not thrown, so every other cell still reports — by probe id and entry point.
    /// </summary>
    private static void AssertPrintsVbcsAnswer(Bk backend, string label, params WideningProbe[] probes)
    {
        TempExec.RequireTool(backend);
        var failures = new List<string>();
        foreach (var probe in probes)
        {
            foreach (var entry in Enum.GetValues<EntryPoint>())
            {
                try
                {
                    var emitted = Emit(backend, entry, ("Main.bas", probe.Source));
                    var got = TempExec.Norm(TempExec.Run(backend, emitted, hangSafe: true));
                    if (got != TempExec.Norm(probe.Vb))
                        failures.Add($"{probe.Id} / {entry}: printed [{got.Replace("\n", " | ")}] where vbc prints [{TempExec.Norm(probe.Vb).Replace("\n", " | ")}]");
                }
                catch (AssertionException ex)
                {
                    failures.Add($"{probe.Id} / {entry}: {ex.Message.Split('\n')[0]}");
                }
            }
        }
        Assert.That(failures, Is.Empty, $"{label} on {backend}, through {string.Join(", ", Enum.GetValues<EntryPoint>())}:\n" + string.Join("\n", failures));
    }

    // ================================================================================================
    // Rows. M1 is killed by every one of them; M3 by the generic row.
    // ================================================================================================

    /// <summary>Stream <- MemoryStream in a declaration, an assignment, an argument and a Return, and the widened value is still the same object (identity, `TypeOf`, `Object`, `IDisposable`).</summary>
    [Test]
    public void AStream_FromAMemoryStream_DeclarationAssignmentArgumentReturn_PrintVbcsAnswer()
        => AssertPrintsVbcsAnswer(Bk.CSharp, "Stream <- MemoryStream", W01, W02, W03, W04, W15);

    /// <summary>⛔ M3. `IEnumerable(Of Integer)` <- `List(Of Integer)`, and `IEnumerable(Of String)` / `ICollection(Of String)` <- `List(Of String)`: the generic mint site.</summary>
    [Test]
    public void AGenericInterface_FromAGenericList_PrintsVbcsAnswer()
        => AssertPrintsVbcsAnswer(Bk.CSharp, "IEnumerable(Of T) / ICollection(Of T) <- List(Of T)", W06, W16);

    /// <summary>A NON-generic interface: `ICollection` <- `ArrayList`, `IDisposable` <- `MemoryStream`.</summary>
    [Test]
    public void ANonGenericInterface_FromAClassImplementingIt_PrintsVbcsAnswer()
        => AssertPrintsVbcsAnswer(Bk.CSharp, "non-generic interface <- implementing class", W07, W13);

    /// <summary>`Exception` <- `ArgumentException` on C#.</summary>
    [Test]
    public void AnExceptionBase_FromASubclass_PrintsVbcsAnswer_OnCSharp()
        => AssertPrintsVbcsAnswer(Bk.CSharp, "Exception <- ArgumentException", W09);

    /// <summary>`Exception` <- `ArgumentException` on MSIL: the one widening MSIL already assembles (W09). SKIPPED with no ilasm, never failed. The rest is held (#283) — see the header.</summary>
    [Test]
    public void AnExceptionBase_FromASubclass_PrintsVbcsAnswer_OnMsil()
        => AssertPrintsVbcsAnswer(Bk.Msil, "Exception <- ArgumentException", W09);

    /// <summary>The container a widened value goes into (`List(Of Stream)` holding a MemoryStream) and the explicit way back down (`CType(s, MemoryStream)`): both still run beside the widening.</summary>
    [Test]
    public void AListOfStream_HoldingAMemoryStream_AndACTypeNarrowing_PrintVbcsAnswer()
        => AssertPrintsVbcsAnswer(Bk.CSharp, "List(Of Stream) and CType narrowing", W12, W11);

    /// <summary>
    /// ⭐ The IDE's multi-file route: the .NET types sit in ANOTHER file's signatures (`MakeMs() As MemoryStream`, `MakeList() As List(Of Integer)`, `MakeError() As ArgumentException`,
    /// `Fill(s As Stream)`). Compiled in each order of the two files through `CompileProjectFiles` (resolution armed), C# run in a child process. vbc's answer, measured on the two files
    /// concatenated into one module: 2, 3, 3, 7, boom.
    /// </summary>
    [Test]
    public void TwoFiles_WithTheNetTypesInTheSiblingsSignatures_PrintVbcsAnswer()
    {
        var util = ("Util.bas", """
            Using System.IO
            Function MakeMs() As MemoryStream
                Dim m As New MemoryStream()
                m.WriteByte(1)
                m.WriteByte(2)
                Return m
            End Function
            Function MakeList() As List(Of Integer)
                Dim l As New List(Of Integer)()
                l.Add(3)
                l.Add(4)
                Return l
            End Function
            Function MakeError() As ArgumentException
                Return New ArgumentException("boom")
            End Function
            Sub Fill(s As Stream)
                s.WriteByte(9)
                Console.WriteLine(s.Length)
            End Sub
            """);
        var main = ("Main.bas", """
            Using System.IO
            Sub Main()
                Dim s As Stream = MakeMs()
                Console.WriteLine(s.Length)
                Fill(MakeMs())
                Fill(s)
                Dim e As IEnumerable(Of Integer) = MakeList()
                Dim t As Integer = 0
                For Each x As Integer In e
                    t = t + x
                Next
                Console.WriteLine(t)
                Dim ex As Exception = MakeError()
                Console.WriteLine(ex.Message)
            End Sub
            """);
        const string vb = "2\n3\n3\n7\nboom";

        var failures = new List<string>();
        foreach (var order in new[] { new[] { util, main }, new[] { main, util } })
        {
            var label = string.Join(",", order.Select(f => f.Item1));
            try
            {
                var emitted = Emit(Bk.CSharp, EntryPoint.ProjectRelease, order);
                var got = TempExec.Norm(TempExec.Run(Bk.CSharp, emitted, hangSafe: true));
                if (got != TempExec.Norm(vb))
                    failures.Add($"[{label}] printed [{got.Replace("\n", " | ")}] where vbc prints [{vb.Replace("\n", " | ")}]");
            }
            catch (AssertionException ex)
            {
                failures.Add($"[{label}] {ex.Message.Split('\n')[0]}");
            }
        }
        Assert.That(failures, Is.Empty, "two files, .NET types in the sibling signatures, through CompileProjectFiles:\n" + string.Join("\n", failures));
    }
}

/// <summary>
/// #194 FRONT END, fast: no process, no compile of generated code. The resolver is armed the way the C# CLI arms it (`ConfigureNetResolution(factory, nativeBackend: false)`). Two halves: the widenings the
/// front end now ACCEPTS (the fast subset skips the Integration fixture above, so this is the only positive row it sees), and vbc's own refusals, which must STAY refused.
/// </summary>
[TestFixture]
public class NetSubtypeWideningFrontEndTests
{
    /// <summary>One resolver for the fixture: construction reads ~170 assemblies.</summary>
    private static readonly Lazy<NetTypeResolver> SharedResolver =
        new(() => NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths));

    /// <summary>Parse and analyze with .NET resolution armed; the semantic errors, or an empty list when the program is accepted.</summary>
    private static List<string> Refusals(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value, nativeBackend: false);
        analyzer.Analyze(ast);
        return analyzer.Errors.Select(e => e.Message).ToList();
    }

    /// <summary>
    /// ⛔ M1 and M3 (and the only positive row the fast subset sees). A class, a generic interface (W06: the generic mint site) and an Exception base are accepted — each program, on its own, with no error.
    /// </summary>
    [Test]
    public void WideningIsAccepted_ForAClass_AGenericInterface_AndAnExceptionBase()
    {
        var failures = new List<string>();
        foreach (var probe in new[]
                 {
                     NetSubtypeWideningExecutionTests.W01, NetSubtypeWideningExecutionTests.W06, NetSubtypeWideningExecutionTests.W09,
                     NetSubtypeWideningExecutionTests.W13, NetSubtypeWideningExecutionTests.W16,
                 })
        {
            var errors = Refusals(probe.Source);
            if (errors.Count > 0) failures.Add($"{probe.Id}: {string.Join(" | ", errors)}");
        }
        Assert.That(failures, Is.Empty, "vbc widens these under Option Strict On, so the front end must accept them:\n" + string.Join("\n", failures));
    }

    /// <summary>⛔ M2. W10: the implicit NARROWING `Dim m As MemoryStream = s` (s is a Stream) stays refused — vbc's BC30512 under Option Strict On.</summary>
    [Test]
    public void ANarrowing_IsRefused_AsVbcRefusesIt()
    {
        var errors = Refusals("""
            Using System.IO
            Sub Main()
                Dim s As Stream = New MemoryStream()
                Dim m As MemoryStream = s
                Console.WriteLine(m.Length)
            End Sub
            """);
        Assert.That(string.Join(" | ", errors), Does.Contain("Cannot assign value of type 'Stream' to variable of type 'MemoryStream'"),
            "vbc: BC30512 'Option Strict On disallows implicit conversions from Stream to MemoryStream'");
    }

    /// <summary>W14: an unrelated pair, `Dim s As Stream = New ArrayList()`, stays refused — vbc's BC30311.</summary>
    [Test]
    public void AnUnrelatedPair_StreamFromArrayList_IsRefused_AsVbcRefusesIt()
    {
        var errors = Refusals("""
            Using System.IO
            Using System.Collections
            Sub Main()
                Dim s As Stream = New ArrayList()
                Console.WriteLine("bad")
            End Sub
            """);
        Assert.That(string.Join(" | ", errors), Does.Contain("Cannot assign value of type 'ArrayList' to variable of type 'Stream'"),
            "vbc: BC30311 'Value of type ArrayList cannot be converted to Stream'");
    }

    /// <summary>
    /// More unrelated and narrowing pairs, each one program, each measured against vbc: an `IEnumerable(Of Integer)` from a `List(Of String)` (BC36754: the type argument does not widen), a `List(Of Integer)`
    /// from a `HashSet(Of Integer)` (BC30311), an `Exception` from a `MemoryStream` (BC30311), and a `List(Of Integer)` from an `IEnumerable(Of Integer)` (BC30512: a generic narrowing, M2's other kill).
    /// </summary>
    [Test]
    public void AnUnrelatedOrNarrowingPair_OfGenericAndExceptionTypes_IsRefused_AsVbcRefusesIt()
    {
        var cases = new (string Id, string Source, string Message)[]
        {
            ("IEnumerable(Of Integer) <- List(Of String)",
                "Sub Main()\n    Dim e As IEnumerable(Of Integer) = New List(Of String)()\n    Console.WriteLine(\"bad\")\nEnd Sub\n",
                "Cannot assign value of type 'List' to variable of type 'IEnumerable'"),
            ("List(Of Integer) <- HashSet(Of Integer)",
                "Sub Main()\n    Dim l As List(Of Integer) = New HashSet(Of Integer)()\n    Console.WriteLine(\"bad\")\nEnd Sub\n",
                "Cannot assign value of type 'HashSet' to variable of type 'List'"),
            ("Exception <- MemoryStream",
                "Using System.IO\nSub Main()\n    Dim e As Exception = New MemoryStream()\n    Console.WriteLine(\"bad\")\nEnd Sub\n",
                "Cannot assign value of type 'MemoryStream' to variable of type 'Exception'"),
            ("List(Of Integer) <- IEnumerable(Of Integer)",
                "Sub Main()\n    Dim l As New List(Of Integer)()\n    Dim e As IEnumerable(Of Integer) = l\n    Dim back As List(Of Integer) = e\n    Console.WriteLine(\"bad\")\nEnd Sub\n",
                "Cannot assign value of type 'IEnumerable' to variable of type 'List'"),
        };

        var failures = new List<string>();
        foreach (var (id, source, message) in cases)
        {
            var errors = string.Join(" | ", Refusals(source));
            if (!errors.Contains(message)) failures.Add($"{id}: expected [{message}], got [{(errors.Length == 0 ? "no error" : errors)}]");
        }
        Assert.That(failures, Is.Empty, "vbc refuses each of these:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// A USER type that shadows a .NET name does not widen to or from the .NET type: a user `Class Stream` is not `System.IO.Stream` (vbc: BC30311 for `Dim s As Stream = New MemoryStream()`), and a user
    /// `Class MemoryStream` is not `System.IO.MemoryStream`, so it is not a `System.IO.Stream` either. The judge never asks the resolver about a user type.
    /// </summary>
    [Test]
    public void AUserTypeThatShadowsANetName_DoesNotWidenToTheNetBase()
    {
        var cases = new (string Id, string Source)[]
        {
            ("user Stream as the TARGET", "Using System.IO\nClass Stream\n    Public N As Integer\nEnd Class\nSub Main()\n    Dim s As Stream = New MemoryStream()\n    Console.WriteLine(\"bad\")\nEnd Sub\n"),
            ("user MemoryStream as the SOURCE", "Using System.IO\nClass MemoryStream\n    Public N As Integer\nEnd Class\nSub Main()\n    Dim s As Stream = New MemoryStream()\n    Console.WriteLine(\"bad\")\nEnd Sub\n"),
        };

        var failures = new List<string>();
        foreach (var (id, source) in cases)
        {
            var errors = string.Join(" | ", Refusals(source));
            if (!errors.Contains("Cannot assign value of type 'MemoryStream' to variable of type 'Stream'"))
                failures.Add($"{id}: expected the refusal, got [{(errors.Length == 0 ? "no error" : errors)}]");
        }
        Assert.That(failures, Is.Empty, "vbc: BC30311 for both — a user type is not the .NET type of the same name:\n" + string.Join("\n", failures));
    }
}
