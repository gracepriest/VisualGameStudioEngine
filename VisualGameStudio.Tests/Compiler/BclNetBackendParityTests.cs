using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// §12.1 parity oracle for programs that CALL .NET — the sibling of
/// <see cref="BclBackendParityTests"/>, which cannot host them.
///
/// <para><b>Why a second fixture and not a wider one.</b> The sibling's C++ leg is
/// <c>BclE2E.CompileToCppOptimized</c> (in-process, combined-mode codegen) handed to a bare
/// single-TU compile. A .NET-calling program emits a TU that <c>#include</c>s
/// <c>blnet_proxies.g.hpp</c>, so that leg dies with <c>fatal error C1083</c> — MEASURED. The
/// tempting fix is to route the sibling's leg through the project build instead. That was
/// measured and REJECTED: all of its rows have an EMPTY .NET surface, so they would pay for the
/// whole shim/proxy pipeline and buy nothing, while eight of them exist ONLY in that table and
/// would lose their combined-mode compile-and-run (<c>CppProjectBuilder</c> emits SPLIT mode,
/// and <c>CppSplitEmissionTests</c> exists precisely because the two modes drift).
///
/// <para>⛔ <b>A unification WILL look free.</b> Swapping the sibling's leg was measured to
/// change the verdict of ZERO of its rows — so a green run is not evidence the swap is safe.
/// What changes is what those rows MEASURE. If someone unifies anyway, first port the
/// parity-only programs into <c>CppBclEndToEndTests</c>' table so they keep a combined-mode
/// compile-and-run.</para></para>
///
/// <para><b>Both legs spawn the shipped <c>BasicLang.exe</c>.</b> The C# leg via
/// <see cref="CliTestHarness.CompileRunCSharp"/>, the native leg via <c>build</c> on a
/// <c>&lt;TargetBackend&gt;Cpp&lt;/TargetBackend&gt;</c> project. Deliberately symmetric: the
/// oracle should be the deployed compiler on both sides, not one binary and one in-process API.
/// Neither leg needs a <c>&lt;NetProxy&gt;</c>, a <c>&lt;Reference&gt;</c> or a
/// <c>&lt;HintPath&gt;</c> — every row here uses framework-only surface, which is also the
/// reason a non-framework assembly cannot be a row (the C# leg's project template carries no
/// <c>&lt;Reference&gt;</c> item).</para>
///
/// <para><b>MANDATORY PROGRAM CONSTRAINTS</b> — these are in addition to the ten in
/// <see cref="BclBackendParityTests"/>, and each was learned by a measured refusal:</para>
/// <list type="number">
/// <item><description><b>A .NET call's RESULT is typed <c>Object</c> on the C# leg and the
/// assignment is REFUSED.</b> <c>Dim d As DateTime = XmlConvert.ToDateTime(...)</c> is a
/// C#-leg build error while the identical line compiles on <c>--target=cpp</c>. Wrap every
/// non-String .NET result in <c>CType(expr, T)</c>. ⛔ That wrapper is load-bearing and NOT
/// generally safe — chip <c>task_0c803e75</c> records <c>CType</c> on an enum member emitting
/// uncompilable C++ — so BUILD every new row, never infer it from these.</description></item>
/// <item><description><b>A .NET-throwing row must catch with <c>Catch ex As Exception</c>
/// only.</b> On the C++ backend every non-<c>Exception</c> clause lowers to
/// <c>catch (const std::runtime_error&amp;)</c> and <c>Visit(IRThrow)</c> throws a bare
/// <c>std::runtime_error</c>, so clause 1 is a catch-all: a discriminating clause would be RED
/// (chip <c>task_d7f0a91c</c> — measured, prints <c>IOE</c> natively where .NET prints
/// <c>EX</c>) and a single non-<c>Exception</c> clause would be GREEN BY ACCIDENT.</description></item>
/// <item><description><b>Only the five curated <c>ManagedOwned</c> names cross</b> —
/// <c>Regex</c>, <c>Uri</c>, <c>Stream</c>, <c>FileInfo</c>, <c>DirectoryInfo</c>
/// (<c>BoundaryTypeRegistry</c>). <c>MemoryStream</c>, <c>FileStream</c> and <c>StringWriter</c>
/// are refused, so a Stream row must spell the static type <c>Stream</c>.</description></item>
/// <item><description><b>Fresh temp directory per row, never shared.</b> The shim cache keys on
/// (project dir, config, surface), so reusing a directory turns the next row's publish into a
/// cache hit — the row still passes but stops proving the generator emitted a working shim for
/// ITS surface. This is why cost is linear at ~100 s/row and cannot be optimized away.</description></item>
/// </list>
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Integration")]
public class BclNetBackendParityTests
{
    /// <summary>
    /// Applied to the RUN of BOTH legs. The native leg's shim IS .NET, so anything the SHIM
    /// formats is culture-sensitive there too.
    ///
    /// <para>⚠ Insurance, not a proven mechanism: the three rows below matched WITHOUT it on the
    /// native side, because <c>XmlConvert</c> is invariant by spec and the <c>ToString()</c>
    /// calls are formatted by the NATIVE runtime rather than the shim. A future row that lets
    /// the shim format a culture-sensitive value is the one that would find out.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> InvariantCultureEnv =
        new Dictionary<string, string> { ["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1" };

    /// <summary>
    /// Probed ONCE, up front. Without this every row would pay a full C# build and run before
    /// discovering the native leg cannot run at all. Note a missing C++ toolchain surfaces here
    /// as a nonzero CLI exit carrying BL6005, not as the <c>Assert.Ignore</c> the sibling
    /// fixture's in-process leg raises from inside the leg.
    /// </summary>
    [OneTimeSetUp]
    public void RequireNativeDotNetToolchain()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("the Native-AOT shim publish recipe is pinned win-x64.");
        if (!CliTestHarness.DotnetOnPath())
            Assert.Ignore("dotnet SDK not found on PATH — the shim cannot be published.");
        if (BasicLang.Compiler.ProjectSystem.CppToolchain.Find() == null)
            Assert.Ignore("no C++ toolchain found — the native leg cannot link.");
    }

    private static (string CSharp, string Cpp) RunBothBackends(string blSource)
    {
        var csOut = CliTestHarness.CompileRunCSharp(blSource, InvariantCultureEnv);
        var cppOut = CompileRunNativeWithNet(blSource);
        return (csOut.Replace("\r\n", "\n"), cppOut.Replace("\r\n", "\n"));
    }

    /// <summary>
    /// Build and run a .NET-calling program natively, through the shipped CLI.
    ///
    /// <para>⛔ The build timeout is 300 s, NOT the 120 s the C# leg uses. Native builds here
    /// were measured at 80-98 s in Debug and 126 s in Release, so 120 s WILL trip on a loaded
    /// machine — and a timeout surfaces as an assertion failure inside a fixture whose entire
    /// message vocabulary is "semantic divergence", which is the worst possible way to
    /// mis-report machine load. No <c>-c</c> is passed (i.e. Debug), matching the C# leg, and
    /// Debug also measured FASTER than Release here.</para>
    ///
    /// <para>⛔ The exe is run IN PLACE. The AOT shim DLL sits beside it and the program dies
    /// with <c>os error 126</c> if it is moved away.</para>
    /// </summary>
    private static string CompileRunNativeWithNet(string blSource)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-netparity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.bas"), blSource);
            var projPath = Path.Combine(dir, "App.blproj");
            File.WriteAllText(projPath, @"<Project>
  <PropertyGroup>
    <ProjectName>App</ProjectName>
    <OutputType>Exe</OutputType>
    <TargetBackend>Cpp</TargetBackend>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Main.bas"" />
  </ItemGroup>
</Project>");

            var build = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { "build", projPath }, dir, timeoutMs: 300_000);
            Assert.That(build.ExitCode, Is.Zero,
                "native .NET build failed.\nSTDOUT:\n" + build.StdOut + "\nSTDERR:\n" + build.StdErr
                + DescribeGeneratedSources(dir));

            var exes = Directory.GetFiles(dir, "App.exe", SearchOption.AllDirectories);
            Assert.That(exes, Is.Not.Empty,
                "native build reported success but produced no App.exe." + DescribeGeneratedSources(dir));

            var run = CliTestHarness.RunProcess(
                exes[0], Array.Empty<string>(), Path.GetDirectoryName(exes[0])!,
                timeoutMs: 60_000, environment: InvariantCultureEnv);
            Assert.That(run.ExitCode, Is.Zero,
                "the native program exited " + run.ExitCode + ".\nSTDOUT:\n" + run.StdOut
                + "\nSTDERR:\n" + run.StdErr + DescribeGeneratedSources(dir));

            return run.StdOut;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Emitted C++ for a failure message. This restores what the sibling fixture gets free from
    /// <c>BclE2E.WithoutRuntimeInFailures</c>: without it a red row reports a normalized
    /// diagnostic while the evidence sits in an <c>obj/gen</c> the <c>finally</c> is about to
    /// delete — and a 100-second row is exactly the kind nobody re-runs by hand.
    ///
    /// <para>⛔ Must be called BEFORE the delete, i.e. inside the assertion message, which is
    /// why every <c>Assert</c> above concatenates it eagerly.</para>
    /// </summary>
    private static string DescribeGeneratedSources(string projectDir)
    {
        try
        {
            var gen = Directory.GetDirectories(projectDir, "gen", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (gen == null) return "\n(no obj/gen directory was produced)";

            var listing = string.Join("\n  ", Directory.GetFiles(gen).Select(Path.GetFileName));
            var main = Path.Combine(gen, "App.main.g.cpp");
            var body = File.Exists(main)
                ? "\n--- App.main.g.cpp ---\n" + File.ReadAllText(main)
                : "";
            return "\n--- obj/gen ---\n  " + listing + body;
        }
        catch (Exception ex)
        {
            return "\n(could not read obj/gen: " + ex.Message + ")";
        }
    }

    // ------------------------------------------------------------------
    // Programs
    // ------------------------------------------------------------------

    /// <summary>
    /// §12.1 #1 — the §6.4 conversion pairs round-tripped through REAL .NET calls. Five of the
    /// six pairs (DateTime, TimeSpan, Decimal, Guid, DateTimeOffset); four round-trip in both
    /// directions, Guid is argument-only, matching the wire rows Task 8c landed.
    ///
    /// <para>The tick counts and the scale-preserving <c>12.3450</c> are the point: they pin the
    /// epoch and the Decimal bit layout ACROSS THE WIRE, which a value-only comparison would
    /// not. <c>XmlConvert</c> is deliberate — it is invariant by spec, so the row cannot pass or
    /// fail on culture.</para>
    ///
    /// <para>The sixth pair, StringBuilder, is parity-UNREACHABLE and is not an omission: the
    /// only framework vehicle (<c>StringWriter</c>) is not a <c>ManagedOwned</c> name, and §6.4
    /// crosses StringBuilder BY VALUE while the C# leg has it by reference — so any mutating
    /// vehicle would diverge by construction. It stays covered by <c>NetProxyStubRunTests</c>.</para>
    /// </summary>
    private const string NetWireRoundTripProgram = @"Using System.Xml

Module M
 Sub Main()
  Dim d As New DateTime(2026, 1, 2, 3, 4, 5)
  Console.WriteLine(XmlConvert.ToString(d, XmlDateTimeSerializationMode.Utc))
  Dim dBack As DateTime = CType(XmlConvert.ToDateTime(""2026-03-04T05:06:07Z"", XmlDateTimeSerializationMode.Utc), DateTime)
  Console.WriteLine(dBack.ToString(""yyyy-MM-dd HH:mm:ss""))
  Console.WriteLine(dBack.Ticks)
  Dim ts As New TimeSpan(1, 2, 30, 15)
  Console.WriteLine(XmlConvert.ToString(ts))
  Dim tsBack As TimeSpan = CType(XmlConvert.ToTimeSpan(""PT2H15M30S""), TimeSpan)
  Console.WriteLine(tsBack.ToString())
  Console.WriteLine(tsBack.Ticks)
  Dim money As Decimal = 19.99
  Console.WriteLine(XmlConvert.ToString(money))
  Dim moneyBack As Decimal = CType(XmlConvert.ToDecimal(""12.3450""), Decimal)
  Console.WriteLine(moneyBack)
  Dim known As Guid = Guid.Parse(""6f9619ff-8b86-d011-b42d-00c04fc964ff"")
  Console.WriteLine(XmlConvert.ToString(known))
  Dim clock As New DateTime(2026, 7, 28, 12, 0, 0)
  Dim dto As New DateTimeOffset(clock, TimeSpan.FromHours(-5))
  Console.WriteLine(XmlConvert.ToString(dto))
  Dim dtoBack As DateTimeOffset = CType(XmlConvert.ToDateTimeOffset(""2026-03-04T05:06:07+02:00""), DateTimeOffset)
  Console.WriteLine(dtoBack.ToString())
  Console.WriteLine(dtoBack.ToUnixTimeSeconds())
 End Sub
End Module";

    /// <summary>
    /// §12.1 #2 and #3 MERGED — a throwing .NET call caught around the boundary, AND a local
    /// <c>Throw</c> caught by a subclass clause with real control flow in the catch body, in one
    /// file that also calls .NET. Merging is the "fewer, richer programs" lever: each row costs
    /// a cold ~90 s shim publish that cannot be amortized, so two programs would cost double for
    /// no extra coverage.
    ///
    /// <para>⛔ The .NET throw is caught by <c>Catch e As Exception</c> DELIBERATELY. A
    /// discriminating clause would be RED against the live catch-all miscompile (chip
    /// <c>task_d7f0a91c</c>), and a single non-<c>Exception</c> clause would be GREEN BY
    /// ACCIDENT. Neither is a parity result.</para>
    /// </summary>
    private const string NetThrowAndCatchProgram = @"Using System.Xml

Module M
 Sub Main()
  Dim money As Decimal = 1.5
  Dim two As Decimal = 2
  Console.WriteLine(XmlConvert.ToString(money))
  Try
   Dim bad As DateTime = CType(XmlConvert.ToDateTime(""not-a-date"", XmlDateTimeSerializationMode.Utc), DateTime)
   Console.WriteLine(bad.Ticks)
  Catch e As Exception
   Console.WriteLine(""CAUGHT-NET"")
  End Try
  Try
   Throw New ArgumentNullException(""boom"")
  Catch e As ArgumentException
   Console.WriteLine(""SUBCLASS-MATCH"")
   If money < two Then
    Console.WriteLine(""catch-branch-then"")
   Else
    Console.WriteLine(""catch-branch-else"")
   End If
   For i As Integer = 1 To 3
    Console.WriteLine(i)
   Next
  Catch b As Exception
   Console.WriteLine(""EX"")
  End Try
  Console.WriteLine(""after"")
 End Sub
End Module";

    /// <summary>
    /// §12.1 #7 — <c>ToString()</c> on a handle whose type does NOT override it, which is D-P1's
    /// actual proof: the first line comes back <c>System.IO.Stream+NullStream</c>, the RUNTIME
    /// type's name, which no native reimplementation could invent. Plus a <c>Uri</c> whose
    /// members cross back as strings.
    ///
    /// <para>⛔ The static type must be spelled <c>Stream</c>. <c>MemoryStream</c> and
    /// <c>FileStream</c> are REFUSED — <c>BoundaryTypeRegistry.ManagedOwned</c> is a curated
    /// five-name set, which is also why <c>File.Open(path, FileMode.Open)</c> cannot work.</para>
    ///
    /// <para>⚠ <c>System.IO.Stream+NullStream</c> is safe as a PARITY assertion — both legs call
    /// real .NET, so a runtime rename moves both together — but it would be brittle as a
    /// hand-written expectation. Do not "fix" it into one.</para>
    /// </summary>
    private const string NetHandleToStringProgram = @"Using System.IO

Module M
 Sub Main()
  Dim s As Stream = CType(Stream.Null, Stream)
  Console.WriteLine(s.ToString())
  Dim u As New Uri(""http://example.com/a/b?q=1"")
  Console.WriteLine(u.ToString())
  Console.WriteLine(u.Host)
  Console.WriteLine(u.AbsolutePath)
 End Sub
End Module";

    private static readonly BclBackendParityTests.ParityProgram[] Programs =
    {
        new BclBackendParityTests.ParityProgram("NetWireRoundTrip", NetWireRoundTripProgram),
        new BclBackendParityTests.ParityProgram("NetThrowAndCatch", NetThrowAndCatchProgram),
        new BclBackendParityTests.ParityProgram("NetHandleToString", NetHandleToStringProgram),
    };

    public static IEnumerable<TestCaseData> ParityCases() =>
        Programs.Select(p => new TestCaseData(p).SetArgDisplayNames(p.Name));

    [TestCaseSource(nameof(ParityCases))]
    public void Backends_PrintIdenticalOutput(BclBackendParityTests.ParityProgram program)
    {
        var (csharp, cpp) = RunBothBackends(program.Source);

        // ⛔ VACUITY GUARD, copied from the sibling fixture. Two identically-EMPTY stdouts
        // satisfy the equality assert below while proving nothing, and a silently-failed
        // 300-second toolchain is exactly the failure mode that produces them.
        Assert.That(csharp, Is.Not.Empty,
            "the C# oracle produced no output — the program printed nothing, so the "
            + "comparison below would pass vacuously.");

        Assert.That(cpp, Is.EqualTo(csharp),
            BclE2E.DescribeFirstDifference(csharp, cpp));
    }
}
