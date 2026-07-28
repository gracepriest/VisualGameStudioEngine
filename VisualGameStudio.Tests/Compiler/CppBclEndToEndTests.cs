using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 9 helper for tests that scan WHOLE generated C++ output with negative
/// assertions (Does.Not.Contain): the P1 BCL runtime bodies are now spliced into
/// EVERY generated program (both emission modes), so legitimate runtime text (e.g.
/// the ostream inserters' <c>v.ToString()</c>, or the word "Integer" in a body
/// comment) would otherwise trip scans aimed at USER-code lowering. Stripping the
/// verbatim spliced bodies keeps those assertions at full strength over the code
/// the test actually targets.
/// </summary>
internal static class CppGeneratedCode
{
    /// <summary>
    /// <paramref name="cpp"/> with the spliced P1 runtime bodies removed and line
    /// endings normalized to '\n' (the splice re-emits the consts line-by-line, so
    /// only line endings can differ from the source consts).
    /// </summary>
    internal static string WithoutBclRuntime(string cpp)
    {
        var norm = cpp.Replace("\r\n", "\n");
        norm = norm.Replace(CppBclRuntime.BclBody.Replace("\r\n", "\n"), "");
        norm = norm.Replace(CppDecimalRuntime.DecimalBody.Replace("\r\n", "\n"), "");
        return norm;
    }
}

/// <summary>
/// P1 Task 9 (spec §12 layer 1 + splice smokes): the native BCL runtime
/// (bl_bcltypes + bl_decimal bodies) is spliced UNCONDITIONALLY into BOTH emission
/// modes — the fast pins below guard presence without a compiler (the classic
/// both-modes drift trap), and the Integration smokes prove the spliced headers
/// actually compile and run inside a generated program in each mode. Task 11
/// extends this fixture with the per-type BL end-to-end battery.
///
/// NonParallelizable, matching the other CLI-spawning fixtures: the cases here
/// spawn BasicLang.exe and a C++ toolchain, and each one already saturates a core.
/// </summary>
[TestFixture]
[NonParallelizable]
public class CppBclEndToEndTests
{
    // ------------------------------------------------------------------
    // Shared compile helpers (same idiom as CppCollectionTests).
    // ------------------------------------------------------------------

    /// <summary>Compile BL source to combined-mode C++ (no optimizer).</summary>
    private static string CompileToCpp(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");
        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(irModule);
    }

    /// <summary>
    /// Compile BL source to combined-mode C++ THROUGH the standard optimizer passes —
    /// the CLI-equivalent path (repo law: validate codegen via the optimizer, not only
    /// the non-optimizing helper).
    /// </summary>
    private static string CompileToCppOptimized(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");

        var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(irModule);

        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(irModule);
    }

    /// <summary>
    /// Compile the generated C++ with a real compiler, run it, return stdout with
    /// line endings normalized. Ignores when no C++ compiler is available.
    /// </summary>
    private static string CompileRun(string cppSource)
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        return WithoutRuntimeInFailures(() => VisualGameStudio.Tests.Native.CppCompile
            .CompileAndRun(cppSource, compiler.Value)).Replace("\r\n", "\n");
    }

    /// <summary>
    /// Run <paramref name="compileAndRun"/>, and on a compile/run assertion failure re-raise
    /// it with the spliced BCL runtime bodies stripped from the message. CppCompile puts the
    /// whole translation unit in its failure context, which since Task 9 means ~1,460 lines
    /// of verbatim runtime header would bury the ~40 lines of user-code lowering that
    /// actually failed. Only AssertionException is intercepted — Assert.Ignore's
    /// IgnoreException (and every real exception) propagates untouched.
    /// </summary>
    private static string WithoutRuntimeInFailures(Func<string> compileAndRun)
    {
        try
        {
            return compileAndRun();
        }
        catch (AssertionException ex)
        {
            throw new AssertionException(CppGeneratedCode.WithoutBclRuntime(ex.Message), ex);
        }
    }

    /// <summary>
    /// The CLI leg (repo law: validate through BOTH entry points). Drives the REAL
    /// <c>BasicLang.exe &lt;file&gt;.bas --target=cpp</c> — the binary the suite deploys next
    /// to the tests, so it always carries the compiler under test — then compiles and runs
    /// the .cpp it wrote.
    ///
    /// This is NOT merely "<see cref="CompileToCppOptimized"/> in another process". The CLI
    /// goes through <c>BasicCompiler.CompileFile</c>: the module registry, preprocessing,
    /// <c>CombineIRModules</c>, and a <c>CppCodeGenerator</c> built with DEFAULT options
    /// (the in-process helper suppresses comments and hand-builds a single-unit IRModule).
    /// The optimizer passes match — <c>CompileFile</c> runs <c>AddStandardPasses</c> — but
    /// everything around them differs, which is exactly why this leg earns its runtime.
    /// </summary>
    private static string CompileRunViaCli(string blSource)
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var dir = Path.Combine(Path.GetTempPath(), "bl-bcl-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var basPath = Path.Combine(dir, "Prog.bas");
            File.WriteAllText(basPath, blSource);

            var (exit, stdout, stderr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { basPath, "--target=cpp" }, dir, timeoutMs: 120_000);
            Assert.That(exit, Is.EqualTo(0),
                $"CLI `BasicLang.exe Prog.bas --target=cpp` failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            var cppPath = Path.Combine(dir, "Prog.cpp");
            Assert.That(File.Exists(cppPath), Is.True,
                $"CLI reported success but wrote no Prog.cpp.\nSTDOUT:\n{stdout}");

            var generated = File.ReadAllText(cppPath);
            return WithoutRuntimeInFailures(() => VisualGameStudio.Tests.Native.CppCompile
                .CompileAndRun(generated, compiler.Value)).Replace("\r\n", "\n");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }

    /// <summary>
    /// Exact-stdout assertion that reports the FIRST DIFFERING LINE by number instead of
    /// NUnit's character index. These programs print 13–19 lines, and mapping a character
    /// offset back to the BL statement that produced it means counting '\n's by hand.
    ///
    /// Deliberately NOT solved by labelling output from inside the BL sources
    /// (<c>"Year=" &amp; d.Year</c>): string concatenation over native-typed operands is the
    /// documented spec §13 gap, out of scope for P1, and using it would change what these
    /// programs test. The diagnosis belongs in the harness, not in the fixtures under test.
    /// </summary>
    private static void AssertLines(string expected, string actual)
    {
        var e = expected.Replace("\r\n", "\n");
        var a = actual.Replace("\r\n", "\n");
        Assert.That(a, Is.EqualTo(e), e == a ? "" : DescribeFirstDifference(e, a));
    }

    private static string DescribeFirstDifference(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        // Every expectation ends with a trailing newline, so Split leaves an empty tail
        // entry; report the human line COUNT rather than the array length.
        var total = e.Length > 0 && e[^1].Length == 0 ? e.Length - 1 : e.Length;
        var context = $"\n--- expected ---\n{expected}--- actual ---\n{actual}";

        for (int i = 0; i < Math.Min(e.Length, a.Length); i++)
        {
            if (e[i] != a[i])
                return $"stdout line {i + 1} of {total}: expected '{e[i]}' but was '{a[i]}'{context}";
        }
        var actualTotal = a.Length > 0 && a[^1].Length == 0 ? a.Length - 1 : a.Length;
        return $"stdout LINE COUNT differs: expected {total} lines, got {actualTotal} " +
               $"(first {Math.Min(e.Length, a.Length)} match){context}";
    }

    /// <summary>Front half mirrors CppSplitEmissionTests.Split: real frontend, then GenerateSplit.</summary>
    private static CppSplitResult Split(bool emitMain, params (string name, string code)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-bcl-splice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = files.Select(f => { var p = Path.Combine(dir, f.name); File.WriteAllText(p, f.code); return p; }).ToList();
            var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp" });
            var result = compiler.CompileProjectFiles(paths);
            Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
            return new CppCodeGenerator().GenerateSplit(
                result.CombinedIR, "Game", result.Units.Select(u => u.IR).ToList(), emitMain);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ------------------------------------------------------------------
    // FAST both-modes pins (no compiler needed) — spec §12 layer 1.
    // ------------------------------------------------------------------

    /// <summary>
    /// Combined mode: a trivial program's single-string output carries the spliced
    /// native BCL runtime (both header bodies), unconditionally.
    /// </summary>
    [Test]
    public void Combined_TrivialProgram_CarriesSplicedBclRuntime()
    {
        var output = CompileToCpp("Sub Main()\n    Console.WriteLine(\"hi\")\nEnd Sub");
        Assert.That(output, Does.Contain("struct DateTime"));
        Assert.That(output, Does.Contain("struct Decimal"));
    }

    /// <summary>
    /// Split mode: BasicLangRuntime.g.h carries the spliced native BCL runtime —
    /// the both-modes drift guard for the combined pin above.
    /// </summary>
    [Test]
    public void Split_RuntimeHeader_CarriesSplicedBclRuntime()
    {
        var r = Split(emitMain: true, ("Logic.bas", "Sub Main()\n    PrintLine \"hi\"\nEnd Sub"));
        var rt = r.Files["BasicLangRuntime.g.h"];
        Assert.That(rt, Does.Contain("struct DateTime"));
        Assert.That(rt, Does.Contain("struct Decimal"));
    }

    // ------------------------------------------------------------------
    // Integration splice smokes: the spliced headers must COMPILE (and run)
    // inside a generated program, in both emission modes.
    // ------------------------------------------------------------------

    /// <summary>
    /// Combined mode through the OPTIMIZER (CLI-equivalent path): a trivial program
    /// now carries the full spliced BCL runtime and must still compile and run —
    /// this is what catches generator-owned include-set gaps and any collision
    /// between the runtime bodies and the always-emitted preamble/user code.
    /// </summary>
    [Test, Category("Integration")]
    public void Combined_TrivialProgram_WithSplicedRuntime_CompilesAndRuns()
    {
        var output = CompileToCppOptimized("Sub Main()\n    Console.WriteLine(\"bcl-splice-ok\")\nEnd Sub");

        Assert.That(CompileRun(output), Is.EqualTo("bcl-splice-ok\n"));
    }

    /// <summary>
    /// Split mode: the runtime header (now carrying the BCL bodies) is included by
    /// multiple translation units — compiling and linking them together proves the
    /// splice is ODR-safe (every out-of-class definition in the bodies is inline)
    /// and that the split include set covers the bodies' needs.
    /// </summary>
    [Test, Category("Integration")]
    public void Split_TrivialProgram_WithSplicedRuntime_CompilesAndRuns()
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var r = Split(emitMain: true,
            ("Logic.bas", "Function Score() As Integer\n    Return 7\nEnd Function"),
            ("App.bas", "Sub Main()\n    PrintLine Score()\nEnd Sub"));

        var stdout = VisualGameStudio.Tests.Native.CppCompile
            .CompileAndRunFiles(r.Files, r.TranslationUnitFileNames, compiler.Value);

        Assert.That(stdout.Trim(), Is.EqualTo("7"));
    }

    // ------------------------------------------------------------------
    // Task 10: the flip. Behaviour that only became reachable when the six
    // types moved to NativeOwned and SByte to Bridged.
    // ------------------------------------------------------------------

    /// <summary>
    /// Spec §14.6: <c>cout &lt;&lt; uint8_t/int8_t</c> streams a CHARACTER — before this
    /// commit <c>Console.WriteLine(byteValued65)</c> printed 'A'. .NET (and the C#
    /// backend) print the NUMBER, so the console lowering widens Byte/SByte args.
    /// This is the ONE deliberately LIVE behaviour change of the Byte signedness fix.
    /// ALL cout surfaces are covered — Console.Write/WriteLine AND the VB Print/PrintLine
    /// statements — so the two cannot drift into printing the same value differently.
    ///
    /// TASK 11 EXTENSION (spec §12 layer 3, the SByte-on-C++ bullet): the tail of the
    /// program adds SByte ARITHMETIC — binary +/-, compound +=, ordering, and the explicit
    /// widening BL requires (`CType(sbyte, Integer)`; implicit SByte→Integer is a front-end
    /// error, and SByte*Integer yields Integer). Sharing this program's compile keeps the
    /// arithmetic coverage free rather than paying for a second Integration compile.
    /// </summary>
    [Test, Category("Integration")]
    public void BytePrinting_IsNumeric_NotCharacter_OnEveryPrintSurface()
    {
        var output = CompileToCppOptimized(@"
Sub Main()
    Dim b As Byte = 65
    Console.WriteLine(b)
    PrintLine b
    Print b
    Console.WriteLine("""")
    Dim s As SByte = -3
    Console.WriteLine(s)
    PrintLine s
    Dim lo As SByte = -3
    Dim hi As SByte = 10
    Dim sum As SByte = lo + hi
    Console.WriteLine(sum)
    Dim diff As SByte = lo - hi
    Console.WriteLine(diff)
    Dim bumped As SByte = lo
    bumped += 5
    Console.WriteLine(bumped)
    If lo < hi Then
        Console.WriteLine(""ordered"")
    End If
    Dim widened As Integer = CType(hi, Integer)
    Console.WriteLine(widened * 10)
End Sub");

        // Console.WriteLine / PrintLine / Print(no newline) + WriteLine("") / … then the
        // SByte arithmetic tail: -3+10, -3-10, -3+=5, ordering, CType-widened 10*10.
        AssertLines("65\n65\n65\n-3\n-3\n7\n-13\n2\nordered\n100\n", CompileRun(output));
    }

    /// <summary>
    /// The Task 10 rider: EVERY Decimal→non-floating conversion on BOTH lowering
    /// routes (CType via IRCast, C* intrinsics via EmitStdLibCall). A raw
    /// <c>static_cast</c>/<c>std::to_string</c> on the BasicLang::Decimal struct is
    /// invalid C++, so each target has an explicit engine-backed lowering.
    /// NOTE — the <c>CInt</c> value pinned here (19, truncating) is the C++ backend's
    /// LONG-STANDING C*-intrinsic convention, shared with Double/Single; the C#
    /// backend emits Convert.ToInt32 (which rounds → 20). That divergence is
    /// PRE-EXISTING and not Decimal-specific; the cross-backend parity oracle
    /// (Task 13) must use CType, not CInt, for integral narrowing.
    /// </summary>
    [Test, Category("Integration")]
    public void DecimalConversions_ToIntegralStringAndBoolean_LowerThroughTheEngine()
    {
        var output = CompileToCppOptimized(@"
Sub Main()
    Dim d As Decimal = 19.99
    Console.WriteLine(CType(d, Integer))
    Console.WriteLine(CType(d, Long))
    Console.WriteLine(CType(d, String))
    Console.WriteLine(CInt(d))
    Console.WriteLine(CStr(d))
    Console.WriteLine(CDbl(d))
    Dim neg As Decimal = -2.9
    Console.WriteLine(CType(neg, Integer))
    Dim zero As Decimal = 0
    If CType(d, Boolean) Then
        Console.WriteLine(""nonzero"")
    End If
    If Not CBool(zero) Then
        Console.WriteLine(""zero"")
    End If
End Sub");

        // Every Decimal source goes through an engine member, never a raw cast.
        var userCode = CppGeneratedCode.WithoutBclRuntime(output);
        Assert.That(userCode, Does.Contain(").ToDouble()"),
            "Decimal→integral/Double must route through ToDouble():\n" + output);
        Assert.That(userCode, Does.Contain(").ToString()"),
            "Decimal→String must route through the engine's ToString():\n" + output);
        Assert.That(userCode, Does.Contain("IsZeroMag()"),
            "Decimal→Boolean must route through the engine's zero test:\n" + output);

        AssertLines(
            // truncate-toward-zero (matching .NET's (int)decimal), scale-preserving
            // ToString, VarR8FromDec ToDouble, and Convert.ToBoolean's `!= 0`.
            "19\n19\n19.99\n19\n19.99\n19.99\n-2\nnonzero\nzero\n",
            CompileRun(output));
    }

    // ==================================================================
    // Task 11 (spec §12 layers 3–4): the per-type BL end-to-end battery.
    //
    // The (program, expected-output) pairing lives in exactly ONE place — the
    // `Programs` table below — and BOTH legs are TestCaseSource-driven from it:
    // the in-process optimizer path (CompileToCppOptimized) and the shipped CLI
    // (BasicLang.exe … --target=cpp), per the repo's both-entry-points law.
    // Adding a row therefore adds BOTH legs; there is no hand-maintained second
    // list that a new program can be silently omitted from. (Task 12 appends its
    // stdlib-date programs to this table.)
    //
    // EVERY expected string below was produced by running the equivalent
    // program on real .NET (PowerShell as the oracle) BEFORE the C++ leg was
    // run — never hand-computed.
    //
    // Program-writing constraints discovered while building these (all
    // PRE-EXISTING backend behaviour, none introduced by P1):
    //  * Locals must not be named `t0`, `t1`, … — those collide with the
    //    generator's temporary names and emit a redefinition (chip filed;
    //    reproduces on a plain `Dim t0 As String` program with no P1 type).
    //  * `Console.WriteLine(aBoolean)` prints 1/0 on C++ vs True/False on .NET,
    //    so every boolean check branches through an If and prints a word.
    //  * Narrowing uses `CType(x, Integer)`; `CInt` truncates on C++ and rounds
    //    on C# (also pre-existing, non-Decimal-specific).
    //  * `.ToString()` never `CStr(nativeValue)` — Task 10's conversion gate
    //    correctly rejects the intrinsic form on the five non-Decimal natives.
    //  * The plan's Decimal bullet lists `++`; it is deliberately NOT covered.
    //    `n++` is silently DROPPED on BOTH backends — pre-existing and
    //    backend-agnostic, so not a P1 concern (chip task_810dc83e): on a plain
    //    Integer `n = 5 : n++` prints 5 rather than 6, and on a Decimal it
    //    increments an unrelated generator temp. Pinning it here would pin the
    //    BUG. `d += 1` is the correct substitute and IS covered.
    // ==================================================================

    /// <summary>
    /// DateTime: construction, every component property, the ostream inserter,
    /// AddDays, the AddMonths day CLAMP, dt−dt → TimeSpan, dt+ts, ordering,
    /// `ToString(fmt)`, a Parse round-trip, and the headline case the old
    /// std::time_t shim could never express: <c>Dim stamp = DateTime.Now</c>
    /// bound to a LOCAL (asserted structurally — a wall clock cannot be pinned).
    /// </summary>
    private const string DateTimeProgram = @"
Sub Main()
    Dim d As New DateTime(2026, 7, 28, 13, 45, 30)
    Console.WriteLine(d.Year)
    Console.WriteLine(d.Month)
    Console.WriteLine(d.Day)
    Console.WriteLine(d.Hour)
    Console.WriteLine(d.Minute)
    Console.WriteLine(d.Second)
    Console.WriteLine(d.DayOfWeek)
    Console.WriteLine(d.DayOfYear)
    Console.WriteLine(d.ToString(""yyyy-MM-dd""))
    Console.WriteLine(d)
    Dim later As DateTime = d.AddDays(5)
    Console.WriteLine(later.ToString(""yyyy-MM-dd""))
    Dim jan31 As New DateTime(2026, 1, 31)
    Dim clamped As DateTime = jan31.AddMonths(1)
    Console.WriteLine(clamped.ToString(""yyyy-MM-dd""))
    Dim gap As TimeSpan = later - d
    Console.WriteLine(gap.Days)
    Dim two As TimeSpan = TimeSpan.FromHours(2)
    Dim shifted As DateTime = d + two
    Console.WriteLine(shifted.ToString(""HH:mm:ss""))
    If later > d Then
        Console.WriteLine(""ordered"")
    End If
    Dim parsed As DateTime = DateTime.Parse(""2026-07-28"")
    Console.WriteLine(parsed.ToString(""yyyy-MM-dd""))
    Dim stamp = DateTime.Now
    If stamp.Year >= 2026 Then
        Console.WriteLine(""now-into-local-ok"")
    End If
End Sub";

    private const string DateTimeExpected =
        // 2026-07-28 is a Tuesday (DayOfWeek 2, .NET numbering) and day 209 of the
        // year; the paren-less WriteLine(d) is the inserter's invariant "G" shape;
        // Jan 31 + 1 month clamps to Feb 28 (2026 is not a leap year).
        "2026\n7\n28\n13\n45\n30\n2\n209\n2026-07-28\n07/28/2026 13:45:30\n" +
        "2026-08-02\n2026-02-28\n5\n15:45:30\nordered\n2026-07-28\nnow-into-local-ok\n";

    /// <summary>
    /// TimeSpan: the FromX factories, components vs totals (the classic
    /// Hours=1/TotalHours=1.5 distinction), the 3- and 4-argument constructors,
    /// Ticks, Zero, unary minus, the compound <c>ts += ts2</c>, `ToString()`'s
    /// invariant "c" format (including the 7-digit fraction), and the inserter.
    /// </summary>
    private const string TimeSpanProgram = @"
Sub Main()
    Dim ts As TimeSpan = TimeSpan.FromMinutes(90)
    Console.WriteLine(ts.Hours)
    Console.WriteLine(ts.Minutes)
    Console.WriteLine(ts.TotalHours)
    Console.WriteLine(ts.TotalMinutes)
    Console.WriteLine(ts.ToString())
    Dim ts2 As TimeSpan = TimeSpan.FromSeconds(90)
    Console.WriteLine(ts2.ToString())
    ts += ts2
    Console.WriteLine(ts.ToString())
    Console.WriteLine(ts)
    Dim full As New TimeSpan(1, 2, 30, 15)
    Console.WriteLine(full.Days)
    Console.WriteLine(full.Hours)
    Console.WriteLine(full.Minutes)
    Console.WriteLine(full.Seconds)
    Console.WriteLine(full.ToString())
    Dim hm As New TimeSpan(2, 15, 0)
    Console.WriteLine(hm.ToString())
    Console.WriteLine(hm.Ticks)
    Dim zero As TimeSpan = TimeSpan.Zero
    Console.WriteLine(zero.ToString())
    Dim couple As TimeSpan = TimeSpan.FromDays(2)
    Console.WriteLine(couple.TotalHours)
    Dim negated As TimeSpan = -hm
    Console.WriteLine(negated.ToString())
    Dim millis As TimeSpan = TimeSpan.FromMilliseconds(1500)
    Console.WriteLine(millis.ToString())
End Sub";

    private const string TimeSpanExpected =
        "1\n30\n1.5\n90\n01:30:00\n00:01:30\n01:31:30\n01:31:30\n" +
        "1\n2\n30\n15\n1.02:30:15\n02:15:00\n81000000000\n00:00:00\n48\n" +
        "-02:15:00\n00:00:01.5000000\n";

    /// <summary>
    /// Guid: NewGuid asserted STRUCTURALLY (two calls differ; the "D" form is 36
    /// characters), a fixed Parse round-trip, the <c>New Guid("…")</c> string
    /// constructor agreeing with Parse, Guid.Empty, the inserter, and — the
    /// spec §6.2 std::hash story — a <c>Dictionary(Of Guid, String)</c> whose
    /// lookup by an EQUAL-BUT-DISTINCT Guid instance finds the same entry.
    /// </summary>
    private const string GuidProgram = @"
Sub Main()
    Dim g1 As Guid = Guid.NewGuid()
    Dim g2 As Guid = Guid.NewGuid()
    If g1 <> g2 Then
        Console.WriteLine(""distinct"")
    End If
    Dim text As String = g1.ToString()
    Console.WriteLine(text.Length)
    Dim known As Guid = Guid.Parse(""6f9619ff-8b86-d011-b42d-00c04fc964ff"")
    Console.WriteLine(known.ToString())
    Console.WriteLine(known)
    Dim built As New Guid(""6f9619ff-8b86-d011-b42d-00c04fc964ff"")
    If built = known Then
        Console.WriteLine(""ctor-eq"")
    End If
    Dim empty As Guid = Guid.Empty
    Console.WriteLine(empty.ToString())
    Dim map As New Dictionary(Of Guid, String)()
    map.Add(known, ""hello"")
    map.Add(empty, ""empty"")
    Console.WriteLine(map.Count)
    Console.WriteLine(map(known))
    Console.WriteLine(map(built))
End Sub";

    private const string GuidExpected =
        "distinct\n36\n6f9619ff-8b86-d011-b42d-00c04fc964ff\n" +
        "6f9619ff-8b86-d011-b42d-00c04fc964ff\nctor-eq\n" +
        "00000000-0000-0000-0000-000000000000\n2\nhello\nhello\n";

    /// <summary>
    /// StringBuilder — the ONE reference type. Proves the overload-ambiguity pin
    /// (<c>Append(anInteger)</c> appends "42", not a promoted double/bool), the
    /// fluent chain returning the SAME builder, Length in UTF-8 bytes, the
    /// inserter, AppendLine's '\n', and the ALIASING contract: two BL variables
    /// naming one builder see each other's mutations (shared_ptr reference
    /// semantics — a value wrapper would print the pre-mutation text).
    /// </summary>
    private const string StringBuilderProgram = @"
Sub Main()
    Dim sb As New StringBuilder()
    sb.Append(""count="")
    sb.Append(42)
    sb.Append("" done"")
    Console.WriteLine(sb.ToString())
    Console.WriteLine(sb.Length)
    Dim other As StringBuilder = sb
    other.Append(""!"")
    Console.WriteLine(sb.ToString())
    Console.WriteLine(other.ToString())
    sb.Clear()
    sb.Append(""a"")
    Console.WriteLine(other.ToString())
    Dim chain As New StringBuilder(""seed:"")
    chain.Append(""x"").Append(7).Append(""y"")
    Console.WriteLine(chain.ToString())
    Console.WriteLine(chain)
    Dim lines As New StringBuilder()
    lines.AppendLine(""one"")
    lines.AppendLine(""two"")
    Console.WriteLine(lines.ToString())
End Sub";

    private const string StringBuilderExpected =
        // "count=42 done" is 13 UTF-8 bytes. After `other.Append("!")` BOTH names
        // print the '!' text, and after `sb.Clear()/Append("a")` `other` prints "a" —
        // the aliasing proof. The AppendLine block ends with its own '\n' plus
        // WriteLine's, hence the blank line.
        "count=42 done\n13\ncount=42 done!\ncount=42 done!\na\n" +
        "seed:x7y\nseed:x7y\none\ntwo\n\n";

    /// <summary>
    /// Decimal — the money program and the exactness battery: 19.99 × 1.08 with
    /// full scale, Round(…, 2), <c>0.1 + 0.2 = 0.3</c> EXACTLY (the headline
    /// binary-floating-point contrast), scale-preserving multiplication
    /// (1.50 × 2.00 → "3.0000"), loop accumulation, exact division, Mod with a
    /// NEGATIVE dividend, compound <c>+= 1</c>, unary minus, <c>CType(int,
    /// Decimal)</c>, and both Decimal→Double routes (CType and CDbl).
    /// </summary>
    private const string DecimalProgram = @"
Sub Main()
    Dim price As Decimal = 19.99
    Dim rate As Decimal = 1.08
    Dim total As Decimal = price * rate
    Console.WriteLine(total.ToString())
    Dim rounded As Decimal = Decimal.Round(total, 2)
    Console.WriteLine(rounded.ToString())
    Dim a As Decimal = 0.1
    Dim b As Decimal = 0.2
    Dim c As Decimal = 0.3
    If a + b = c Then
        Console.WriteLine(""exact"")
    End If
    Dim sum As Decimal = 0
    For i As Integer = 1 To 10
        sum += a
    Next
    Console.WriteLine(sum.ToString())
    Dim scaled As Decimal = 1.50
    Dim scaled2 As Decimal = 2.00
    Dim prod As Decimal = scaled * scaled2
    Console.WriteLine(prod.ToString())
    Dim q As Decimal = 10
    Dim r As Decimal = 4
    Dim quot As Decimal = q / r
    Console.WriteLine(quot.ToString())
    Dim negDividend As Decimal = -7.5
    Dim two As Decimal = 2
    Dim leftover As Decimal = negDividend Mod two
    Console.WriteLine(leftover.ToString())
    Dim bumped As Decimal = price
    bumped += 1
    Console.WriteLine(bumped.ToString())
    Dim minus As Decimal = -price
    Console.WriteLine(minus.ToString())
    Dim seven As Integer = 7
    Dim fromInt As Decimal = CType(seven, Decimal)
    Console.WriteLine(fromInt.ToString())
    Dim asDouble As Double = CType(price, Double)
    Console.WriteLine(asDouble)
    Console.WriteLine(CDbl(price))
    Console.WriteLine(price)
End Sub";

    private const string DecimalExpected =
        // Scale arithmetic is .NET-faithful throughout: 19.99*1.08 keeps scale 4,
        // ten additions of 0.1 give "1.0" (not "1"), 1.50*2.00 gives "3.0000",
        // and -7.5 Mod 2 keeps the DIVIDEND's sign (-1.5), unlike a C remainder
        // on doubles.
        "21.5892\n21.59\nexact\n1.0\n3.0000\n2.5\n-1.5\n20.99\n-19.99\n7\n" +
        "19.99\n19.99\n19.99\n";

    /// <summary>
    /// DateTimeOffset: the two-argument (clock, offset) constructor, the wall-clock
    /// vs UTC split, Offset, ToUnixTimeSeconds/Milliseconds, the BL <c>Ticks</c>
    /// property (clock ticks, mapped to the runtime's TicksValue), the inserter,
    /// FromUnixTimeSeconds, ToOffset, and the UTC-INSTANT equality program: a
    /// 12:00−05:00 value equals a 17:00+00:00 value even though the wall clocks differ.
    /// </summary>
    private const string DateTimeOffsetProgram = @"
Sub Main()
    Dim clock As New DateTime(2026, 7, 28, 12, 0, 0)
    Dim off As TimeSpan = TimeSpan.FromHours(-5)
    Dim dto As New DateTimeOffset(clock, off)
    Console.WriteLine(dto.ToString())
    Console.WriteLine(dto)
    Dim wall As DateTime = dto.DateTime
    Console.WriteLine(wall.ToString(""yyyy-MM-dd HH:mm:ss""))
    Dim utc As DateTime = dto.UtcDateTime
    Console.WriteLine(utc.ToString(""yyyy-MM-dd HH:mm:ss""))
    Dim shownOffset As TimeSpan = dto.Offset
    Console.WriteLine(shownOffset.TotalHours)
    Console.WriteLine(dto.ToUnixTimeSeconds())
    Console.WriteLine(dto.ToUnixTimeMilliseconds())
    Console.WriteLine(dto.Ticks)
    Dim clock2 As New DateTime(2026, 7, 28, 17, 0, 0)
    Dim dto2 As New DateTimeOffset(clock2, TimeSpan.Zero)
    If dto = dto2 Then
        Console.WriteLine(""same-instant"")
    End If
    Dim epoch As DateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(0)
    Console.WriteLine(epoch.ToString())
    Dim shifted As DateTimeOffset = dto.ToOffset(TimeSpan.Zero)
    Console.WriteLine(shifted.ToString())
End Sub";

    private const string DateTimeOffsetExpected =
        "07/28/2026 12:00:00 -05:00\n07/28/2026 12:00:00 -05:00\n" +
        "2026-07-28 12:00:00\n2026-07-28 17:00:00\n-5\n" +
        "1785258000\n1785258000000\n639208368000000000\nsame-instant\n" +
        "01/01/1970 00:00:00 +00:00\n07/28/2026 17:00:00 +00:00\n";

    /// <summary>
    /// Spec §11 end-to-end: the native runtimes signal errors with
    /// <c>std::runtime_error</c>, which is exactly the type the C++ backend's Catch
    /// lowering binds — so a BL <c>Try/Catch</c> around a Decimal divide-by-zero (and
    /// around a malformed Guid.Parse) catches it, prints its marker, and the program
    /// exits 0 instead of terminating. Neither throw is reachable at compile time: the
    /// IR constant folder deliberately never folds Decimal constants (IROptimizer,
    /// spec 6.1), so the division survives the optimizer into the emitted program.
    /// </summary>
    private const string TryCatchProgram = @"
Sub Main()
    Dim one As Decimal = 1
    Dim zero As Decimal = 0
    Try
        Dim r As Decimal = one / zero
        Console.WriteLine(r.ToString())
    Catch ex As Exception
        Console.WriteLine(""CAUGHT-DIV0"")
    End Try
    Try
        Dim bad As Guid = Guid.Parse(""not-a-guid"")
        Console.WriteLine(bad.ToString())
    Catch ex As Exception
        Console.WriteLine(""CAUGHT-GUID"")
    End Try
    Console.WriteLine(""after"")
End Sub";

    private const string TryCatchExpected = "CAUGHT-DIV0\nCAUGHT-GUID\nafter\n";

    /// <summary>
    /// Task 12 (spec §7): the VB date stdlib — <c>Now/Today/Year/Month/Day/Hour/
    /// Minute/Second/DateAdd/DateDiff/FormatDate</c> — plus <c>NewGuid</c>, on the C++
    /// backend. Argument order is the REPO's C# StdLib table, not classic VB's:
    /// <c>DateAdd(date, interval, number)</c> and <c>DateDiff(date1, date2, interval)</c>,
    /// with DateDiff returning Integer.
    ///
    /// The interval-part set is exactly <c>CSharpStdLib.EmitDateAdd/EmitDateDiff</c>'s
    /// (spec §14.3): d, m, y, h, n, s — matched case-INSENSITIVELY (the C# emission calls
    /// <c>.ToLower()</c>), which the <c>"D"</c> and <c>"S"</c> cases pin. An UNRECOGNIZED
    /// interval is not an error on either backend: DateAdd returns the date unchanged and
    /// DateDiff returns 0 (the C# switch's <c>_ =&gt; date</c> / <c>_ =&gt; 0</c> defaults),
    /// which the <c>"zz"</c> cases pin. The reversed <c>DateDiff(later, d, "d")</c> pins
    /// the negative direction's truncation toward zero (-429, not -430).
    ///
    /// <c>Now()</c>/<c>Today()</c>/<c>NewGuid()</c> are nondeterministic, so they are
    /// asserted STRUCTURALLY: the wall clock's year, Today()'s zeroed clock components,
    /// and a Guid.Parse round-trip of the generated string.
    /// </summary>
    private const string StdlibProgram = @"
Sub Main()
    Dim d As New DateTime(2026, 7, 28, 13, 45, 30)
    Console.WriteLine(Year(d))
    Console.WriteLine(Month(d))
    Console.WriteLine(Day(d))
    Console.WriteLine(Hour(d))
    Console.WriteLine(Minute(d))
    Console.WriteLine(Second(d))
    Console.WriteLine(FormatDate(d, ""yyyy-MM-dd""))
    Console.WriteLine(FormatDate(d, ""yyyy-MM-dd HH:mm:ss""))
    Dim byDays As DateTime = DateAdd(d, ""d"", 5)
    Console.WriteLine(FormatDate(byDays, ""yyyy-MM-dd""))
    Dim byMonths As DateTime = DateAdd(d, ""m"", 5)
    Console.WriteLine(FormatDate(byMonths, ""yyyy-MM-dd""))
    Dim byYears As DateTime = DateAdd(d, ""y"", 5)
    Console.WriteLine(FormatDate(byYears, ""yyyy-MM-dd""))
    Dim byHours As DateTime = DateAdd(d, ""h"", 5)
    Console.WriteLine(FormatDate(byHours, ""HH:mm:ss""))
    Dim byMinutes As DateTime = DateAdd(d, ""n"", 5)
    Console.WriteLine(FormatDate(byMinutes, ""HH:mm:ss""))
    Dim bySeconds As DateTime = DateAdd(d, ""s"", 5)
    Console.WriteLine(FormatDate(bySeconds, ""HH:mm:ss""))
    Dim clamped As New DateTime(2026, 1, 31)
    Dim clampedNext As DateTime = DateAdd(clamped, ""m"", 1)
    Console.WriteLine(FormatDate(clampedNext, ""yyyy-MM-dd""))
    Dim upperInterval As DateTime = DateAdd(d, ""D"", 5)
    Console.WriteLine(FormatDate(upperInterval, ""yyyy-MM-dd""))
    Dim unknownInterval As DateTime = DateAdd(d, ""zz"", 5)
    Console.WriteLine(FormatDate(unknownInterval, ""yyyy-MM-dd HH:mm:ss""))
    Dim later As New DateTime(2027, 9, 30, 20, 15, 45)
    Console.WriteLine(DateDiff(d, later, ""d""))
    Console.WriteLine(DateDiff(d, later, ""m""))
    Console.WriteLine(DateDiff(d, later, ""y""))
    Console.WriteLine(DateDiff(d, later, ""h""))
    Console.WriteLine(DateDiff(d, later, ""n""))
    Console.WriteLine(DateDiff(d, later, ""s""))
    Console.WriteLine(DateDiff(d, later, ""S""))
    Console.WriteLine(DateDiff(d, later, ""zz""))
    Console.WriteLine(DateDiff(later, d, ""d""))
    Dim stamp As DateTime = Now()
    If Year(stamp) >= 2026 Then
        Console.WriteLine(""now-ok"")
    End If
    Dim midnight As DateTime = Today()
    Dim clockParts As Integer = Hour(midnight) + Minute(midnight) + Second(midnight)
    If clockParts = 0 Then
        Console.WriteLine(""today-ok"")
    End If
    Dim id As String = NewGuid()
    Dim roundTripped As Guid = Guid.Parse(id)
    If roundTripped.ToString() = id Then
        Console.WriteLine(""newguid-roundtrip-ok"")
    End If
    If Len(id) = 36 Then
        Console.WriteLine(""newguid-len-ok"")
    End If
End Sub";

    private const string StdlibExpected =
        // Components, then the two FormatDate shapes.
        "2026\n7\n28\n13\n45\n30\n2026-07-28\n2026-07-28 13:45:30\n" +
        // DateAdd d/m/y/h/n/s = +5 of each part …
        "2026-08-02\n2026-12-28\n2031-07-28\n18:45:30\n13:50:30\n13:45:35\n" +
        // … then Jan 31 +1 month CLAMPS to Feb 28, "D" == "d", and "zz" is a no-op.
        "2026-02-28\n2026-08-02\n2026-07-28 13:45:30\n" +
        // DateDiff over 429d 06:30:15: days, calendar months (12+2), calendar years,
        // hours/minutes/seconds, the case-insensitive "S", the unknown-interval 0, and
        // the reversed pair truncating toward zero.
        "429\n14\n1\n10302\n618150\n37089015\n37089015\n0\n-429\n" +
        // Structural: Now(), Today()'s zeroed clock, NewGuid() round-trip and length.
        "now-ok\ntoday-ok\nnewguid-roundtrip-ok\nnewguid-len-ok\n";

    // ------------------------------------------------------------------
    // THE TABLE. One row per program; both legs are generated from it.
    // ------------------------------------------------------------------

    /// <summary>
    /// One end-to-end program and the exact stdout it must produce. Public because
    /// NUnit test methods (which must be public) take it as a parameter.
    /// </summary>
    public sealed record BclProgram(string Name, string Source, string Expected);

    /// <summary>
    /// The single source of truth for the end-to-end battery. Every row is run
    /// TWICE — once through the in-process optimizer path and once through the
    /// shipped CLI — because both leg methods below share this one TestCaseSource.
    /// A new program is therefore covered by BOTH entry points the moment it is
    /// added here, and there is no second list to forget.
    /// </summary>
    private static readonly BclProgram[] Programs =
    {
        new BclProgram("DateTime", DateTimeProgram, DateTimeExpected),
        new BclProgram("TimeSpan", TimeSpanProgram, TimeSpanExpected),
        new BclProgram("Guid", GuidProgram, GuidExpected),
        new BclProgram("StringBuilder", StringBuilderProgram, StringBuilderExpected),
        new BclProgram("Decimal", DecimalProgram, DecimalExpected),
        new BclProgram("DateTimeOffset", DateTimeOffsetProgram, DateTimeOffsetExpected),
        new BclProgram("TryCatch", TryCatchProgram, TryCatchExpected),
        new BclProgram("Stdlib", StdlibProgram, StdlibExpected),
    };

    public static IEnumerable<TestCaseData> ProgramCases() =>
        Programs.Select(p => new TestCaseData(p).SetArgDisplayNames(p.Name));

    /// <summary>
    /// Leg 1 — the in-process optimizer path (repo law: validate codegen through the
    /// optimizer, not only the non-optimizing helper).
    /// </summary>
    [TestCaseSource(nameof(ProgramCases)), Category("Integration")]
    public void Optimizer_TargetCpp_ProducesTheExpectedOutput(BclProgram program)
        => AssertLines(program.Expected, CompileRun(CompileToCppOptimized(program.Source)));

    /// <summary>
    /// Leg 2 — the shipped <c>BasicLang.exe … --target=cpp</c> (plan Task 11 step 3 /
    /// repo law: BOTH entry points). A lowering that only works via the test helper —
    /// or only via the CLI — is a real defect this catches.
    /// </summary>
    [TestCaseSource(nameof(ProgramCases)), Category("Integration")]
    public void Cli_TargetCpp_ProducesTheSameProgramOutput(BclProgram program)
        => AssertLines(program.Expected, CompileRunViaCli(program.Source));
}

/// <summary>
/// P1 Task 10, spec §4.1: the member-surface capability pass. These run the
/// CHECKER only (no C++ compiler), so they are fast, not Integration. Task 11
/// extends the set; this fixture pins the classes of diagnostic the flip
/// introduced plus the two riders it resolved.
/// </summary>
[TestFixture]
public class CppNativeBclDiagnosticTests
{
    private static CppCapabilityException AssertRejected(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "expected a C++ CAPABILITY rejection, but semantic analysis failed first: "
            + string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");
        return Assert.Throws<CppCapabilityException>(() =>
            new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(irModule));
    }

    /// <summary>A member outside the curated v1 surface names the type AND the member.</summary>
    [Test]
    public void UnknownNativeMember_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Sub Main()
    Dim d As New DateTime(2026, 1, 1)
    Console.WriteLine(d.ToBinary())
End Sub");
        Assert.That(ex.Message, Does.Contain("DateTime").And.Contain("ToBinary"));
    }

    /// <summary>
    /// Guid.ToByteArray is deliberately NOT on the BL v1 surface (spec §5: its Byte()
    /// return has no pinned C++ mapping) — it must reject, not silently emit the
    /// native out-param overload.
    /// </summary>
    [Test]
    public void GuidToByteArray_IsNotOnTheBlSurface()
    {
        var ex = AssertRejected(@"
Sub Main()
    Dim g As Guid = Guid.NewGuid()
    Console.WriteLine(g.ToByteArray())
End Sub");
        Assert.That(ex.Message, Does.Contain("Guid").And.Contain("ToByteArray"));
    }

    /// <summary>
    /// Task 11: the SAME rejection for EVERY member-bearing native type, not just
    /// DateTime. Each member below EXISTS in .NET but is deliberately left off the
    /// curated v1 surface (spec §5) — three are instance members; Decimal.ToOACurrency
    /// is a .NET STATIC, included precisely because reaching for it with instance
    /// syntax is a plausible user error that must still land on a clean diagnostic
    /// rather than a raw C++ compiler error. Guid.ToByteArray has its own test below
    /// (it carries the extra "the native header has it, the BL surface does not" story).
    /// The member name is passed explicitly rather than parsed out of the call
    /// expression, so a future paren-less case can be added without the helper
    /// throwing while it hunts for a '('.
    /// </summary>
    [TestCase("Dim x As New TimeSpan(1, 0, 0)", "TimeSpan", "x.Multiply(2)", "Multiply")]
    [TestCase("Dim x As New StringBuilder()", "StringBuilder", "x.EnsureCapacity(64)", "EnsureCapacity")]
    [TestCase("Dim x As Decimal = 1", "Decimal", "x.ToOACurrency()", "ToOACurrency")]
    [TestCase("Dim x As New DateTimeOffset(New DateTime(2026, 1, 1))", "DateTimeOffset", "x.AddDays(1)", "AddDays")]
    public void UnknownNativeMember_IsRejectedCleanly_ForEveryNativeType(
        string declaration, string typeName, string call, string memberName)
    {
        var ex = AssertRejected($@"
Sub Main()
    {declaration}
    Console.WriteLine({call})
End Sub");
        Assert.That(ex.Message, Does.Contain(typeName).And.Contain(memberName));
        Assert.That(ex.Message, Does.Contain("has no native member"));
    }

    /// <summary>An arity outside the surface's overload list is named explicitly.</summary>
    [Test]
    public void UnknownConstructorArity_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Sub Main()
    Dim g As New Guid()
    Console.WriteLine(g.ToString())
End Sub");
        Assert.That(ex.Message, Does.Contain("New Guid").And.Contain("1 argument"));
    }

    /// <summary>
    /// Task 11: the NON-zero-arg ctor-arity case. DateTime's v1 surface carries the
    /// (y,m,d) and (y,m,d,h,mi,s) overloads only, so the .NET 2-argument shape (which
    /// does not exist there either) must be named with the arities that DO work rather
    /// than emitting an unresolvable constructor call.
    /// </summary>
    [Test]
    public void UnsupportedConstructorArity_NamesTheAritiesThatWork()
    {
        var ex = AssertRejected(@"
Sub Main()
    Dim d As New DateTime(2026, 7)
    Console.WriteLine(d.Year)
End Sub");
        Assert.That(ex.Message, Does.Contain("New DateTime"));
        Assert.That(ex.Message, Does.Contain("3 or 6 argument(s), not 2"));
    }

    /// <summary>
    /// Task 11: an arity mismatch on a METHOD (not a constructor). Decimal's v1 surface
    /// pins ToString to the parameterless overload — .NET's format-string overload
    /// (`d.ToString("F2")`) is out of scope, and must say so instead of emitting a
    /// one-argument call the engine has no signature for. DateTime and Guid DO carry
    /// the (0,1) pair, so this is a per-type fact the surface table owns.
    /// </summary>
    [Test]
    public void UnsupportedMethodArity_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Sub Main()
    Dim d As Decimal = 19.99
    Console.WriteLine(d.ToString(""F2""))
End Sub");
        Assert.That(ex.Message, Does.Contain("Decimal.ToString"));
        Assert.That(ex.Message, Does.Contain("0 argument(s), not 1"));
    }

    /// <summary>
    /// Task 11: the KIND mismatch. `d.Year` is a property on the surface and lowers
    /// through the property→method bridge; written WITH parentheses it arrives as an
    /// IRInstanceMethodCall, which the bridge cannot express. The diagnostic tells the
    /// user the one-word fix. (The dotted STATIC property form — `DateTime.Now()` — is
    /// deliberately exempt; ParenthesizedStaticProperty_IsAccepted guards that.)
    /// </summary>
    [Test]
    public void InstancePropertyCalledWithParentheses_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Sub Main()
    Dim d As New DateTime(2026, 1, 1)
    Console.WriteLine(d.Year())
End Sub");
        Assert.That(ex.Message, Does.Contain("DateTime.Year"));
        Assert.That(ex.Message, Does.Contain("drop the parentheses"));
    }

    /// <summary>
    /// LEAK CLOSURE (spec §4.1): a Rejected type used ONLY in expression position never
    /// declares a typed slot, so CheckType never saw it and the construction reached
    /// codegen as an undefined C++ type name (BL6006-class raw failure).
    /// </summary>
    [Test]
    public void RejectedTypeInExpressionPosition_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Sub Main()
    Console.WriteLine(New Regex(""x""))
End Sub");
        Assert.That(ex.Message, Does.Contain("Regex").And.Contain("no C++ mapping"));
    }

    /// <summary>
    /// Task 10 rider (c): before the flip a user-defined `Class Guid` was name-rejected
    /// as an unmapped .NET type; after it, MapType would SILENTLY remap it to
    /// BasicLang::Guid. BasicLang has no namespace to disambiguate with, so the honest
    /// answer is an explicit rename request.
    /// </summary>
    [Test]
    public void UserTypeShadowingNativeBcl_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Class Guid
    Public X As Integer
End Class

Sub Main()
    Dim g As New Guid()
    g.X = 5
    Console.WriteLine(g.X)
End Sub");
        Assert.That(ex.Message, Does.Contain("'Guid' conflicts with a native BCL type"));
        Assert.That(ex.Message, Does.Contain("rename"));
    }

    /// <summary>
    /// Task 10 rider (a), rejection half: a conversion the engine does NOT define must
    /// fail here, never as a raw static_cast on a struct at the C++ compiler.
    /// </summary>
    [Test]
    public void UnsupportedNativeConversion_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Sub Main()
    Dim d As New DateTime(2026, 1, 1)
    Dim n As Integer = CType(d, Integer)
    Console.WriteLine(n)
End Sub");
        Assert.That(ex.Message, Does.Contain("DateTime").And.Contain("Integer"));
        Assert.That(ex.Message, Does.Contain("not supported on the C++ backend"));
    }

    /// <summary>
    /// The C* intrinsics lower through EmitStdLibCall, NOT Visit(IRCast), so the IRCast
    /// gate above never saw them: <c>CStr(dt)</c> emitted <c>to_string(dt)</c> and
    /// <c>CDbl(dt)</c> emitted <c>static_cast&lt;double&gt;(dt)</c> — both clean through
    /// the BasicLang pipeline, both MSVC errors. Only Decimal was guarded (it has
    /// lowerings); the other five native types must reject.
    /// </summary>
    [TestCase("CStr", "String")]
    [TestCase("CDbl", "Double")]
    [TestCase("CInt", "Integer")]
    [TestCase("CBool", "Boolean")]
    public void ConversionIntrinsicOnNonDecimalNativeType_IsRejectedCleanly(
        string intrinsic, string targetName)
    {
        var ex = AssertRejected($@"
Sub Main()
    Dim d As New DateTime(2026, 1, 1)
    Console.WriteLine({intrinsic}(d))
End Sub");
        Assert.That(ex.Message, Does.Contain("DateTime").And.Contain(targetName));
        Assert.That(ex.Message, Does.Contain("not supported on the C++ backend"));
    }

    /// <summary>
    /// A static call arrives as an IRCall with a DOTTED FunctionName — neither the
    /// IRInstanceMethodCall nor the IRFieldAccess arm saw it. An unknown static fell
    /// through the generator's surface dispatch to a flattened phantom function
    /// (<c>t0 = DateTimeFromBinary(5);</c> against a <c>void*</c> temp).
    /// </summary>
    [Test]
    public void UnknownStaticMethod_IsRejectedCleanly()
    {
        var ex = AssertRejected(@"
Sub Main()
    Console.WriteLine(DateTime.FromBinary(5))
End Sub");
        Assert.That(ex.Message, Does.Contain("DateTime").And.Contain("FromBinary"));
        Assert.That(ex.Message, Does.Contain("has no native member"));
    }

    /// <summary>
    /// GUARD for the caveat in the new IRCall arm: the generator DELIBERATELY lowers the
    /// parenthesized static-PROPERTY form through the same static dispatch as the
    /// paren-less one, so the member pass must not reject call syntax on a
    /// StaticProperty. Zero-arg static METHODS (Guid.NewGuid) ride the same path.
    /// </summary>
    [Test]
    public void ParenthesizedStaticProperty_IsAccepted()
    {
        var source = @"
Sub Main()
    Dim d As DateTime = DateTime.Now()
    Dim g As Guid = Guid.NewGuid()
    Console.WriteLine(d.Year)
    Console.WriteLine(g.ToString())
End Sub";
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");
        var output = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(irModule);
        Assert.That(output, Does.Contain("BasicLang::DateTime::Now()"));
        Assert.That(output, Does.Contain("BasicLang::Guid::NewGuid()"));
    }
}
