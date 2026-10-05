using System.Text;
using System.Text.RegularExpressions;
using BasicLang.Compiler.ProjectSystem;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;
using CoreBuildConfiguration = VisualGameStudio.Core.Models.BuildConfiguration;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// One BasicLang program, and what vbc prints for it (task #134's probes). Each one is a program
/// where the AGGRESSIVE optimizer pipeline visibly changes the generated C++ — an algebraic
/// rewrite, or a loop-invariant hoist — so "which pipeline did this build run?" is readable from
/// <c>obj/gen</c> alone.
/// </summary>
public sealed record CppPipelineProgram(string Name, string Source, string Expected)
{
    public override string ToString() => Name;
}

/// <summary>
/// ⭐ THE ENTRY POINTS A C++ <c>.blproj</c> CAN BE BUILT THROUGH. Task #134: all of them reach
/// <c>CppProjectBuilder.EmitCore</c>, and EmitCore built its <c>CompilerOptions</c> without
/// <c>OptimizeAggressive</c>, so a Release C++ project ran the STANDARD pipeline everywhere.
/// </summary>
public enum CppProjectRoute
{
    /// <summary><c>CppProjectBuilder.Build</c>, which the CLI's <c>build</c> and the IDE both call.</summary>
    BuildApi,
    /// <summary>The IDE: <c>BuildService.BuildProjectAsync</c> (spawns the toolchain probe's <c>--version</c>s).</summary>
    IdeBuildService,
    /// <summary>What clangd sees: <c>IntelliSenseEmitter.Emit</c> (no build, no toolchain).</summary>
    IntelliSense,
    /// <summary>The IDE's caller of the above: <c>IntelliSenseEmissionService.RequestEmit</c> (what project open and save fire).</summary>
    IntelliSenseService,
    /// <summary>The real <c>BasicLang build App.blproj -c &lt;config&gt;</c>, spawned.</summary>
    Cli,
}

/// <summary>What a route left in <c>obj/gen</c>, and what it said about it.</summary>
internal sealed record CppProjectEmission(
    IReadOnlyDictionary<string, string> Files, string Diagnostics, string Summary)
{
    /// <summary>Every generated file but the runtime header (a constant ~1,500 lines), one string.</summary>
    public string UserCode => string.Join("\n", Files
        .Where(kv => !kv.Key.Equals("BasicLangRuntime.g.h", StringComparison.OrdinalIgnoreCase))
        .OrderBy(kv => kv.Key, StringComparer.Ordinal)
        .Select(kv => kv.Value));
}

/// <summary>
/// A temp C++ <c>.blproj</c> (one <c>Main.bas</c>, <c>&lt;TargetBackend&gt;Cpp&lt;/TargetBackend&gt;</c>) that
/// can be driven through every <see cref="CppProjectRoute"/> and read back from <c>obj/gen</c>.
///
/// <para>⭐ <b>WHY <c>obj/gen</c> IS THE WITNESS.</b> A BasicLang native build ALWAYS needs MSVC
/// (<c>ProjectFile.EffectiveCppToolchain</c>), so off Windows <c>CppProjectBuilder</c> runs every
/// phase through the C++ codegen, WRITES <c>obj/gen</c>, and only then fails BL6015 at the toolchain
/// gate. The generated C++ is therefore observable on Linux with no compiler at all — which is
/// what lets these tests tell the two pipelines apart without compiling anything. The
/// <see cref="CppProjectRoute.BuildApi"/> and <see cref="CppProjectRoute.IdeBuildService"/> routes
/// pass a toolchain resolver that finds nothing, so on Windows they stop at the same gate instead
/// of invoking MSVC.</para>
///
/// <para>Each <see cref="Emit"/> deletes <c>obj/gen</c> first: a route that fails BEFORE it writes
/// (a transpile error) would otherwise leave the previous emission behind for the assertion to
/// read.</para>
/// </summary>
internal sealed class CppProjectProbe : IDisposable
{
    public string Dir { get; }
    public string BlprojPath => Path.Combine(Dir, "App.blproj");
    public string ObjGen => Path.Combine(Dir, "obj", "gen");

    /// <param name="source">The program: written as <c>Main.bas</c>.</param>
    /// <param name="configurationGroups">
    /// Extra <c>&lt;PropertyGroup Condition="'$(Configuration)' == 'X'"&gt;</c> elements, written after
    /// the project's own PropertyGroup. Empty = the default project (Debug and Release as
    /// <c>ProjectFile</c> declares them).
    /// </param>
    public CppProjectProbe(string source, string configurationGroups = "")
    {
        Dir = Path.Combine(Path.GetTempPath(), "bl-cpppipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, "Main.bas"), source.TrimEnd('\n') + "\n");
        File.WriteAllText(BlprojPath, $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
            {configurationGroups}
            </BasicLangProject>
            """);
    }

    /// <summary>A <c>Condition="'$(Configuration)' == 'name'"</c> PropertyGroup with an <c>&lt;Optimize&gt;</c> (and optionally <c>&lt;DebugSymbols&gt;</c>).</summary>
    public static string Configuration(string name, bool? optimize, bool? debugSymbols = null)
    {
        var body = new StringBuilder();
        if (optimize is { } o) body.Append($"<Optimize>{(o ? "true" : "false")}</Optimize>");
        if (debugSymbols is { } d) body.Append($"<DebugSymbols>{(d ? "true" : "false")}</DebugSymbols>");
        return $"<PropertyGroup Condition=\" '$(Configuration)' == '{name}' \">{body}</PropertyGroup>\n";
    }

    public ProjectFile Load() => ProjectFile.Load(BlprojPath);

    public void Dispose()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(Dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    public IReadOnlyDictionary<string, string> ReadObjGen() =>
        Directory.Exists(ObjGen)
            ? Directory.GetFiles(ObjGen).ToDictionary(Path.GetFileName, File.ReadAllText)!
            : new Dictionary<string, string>();

    /// <summary>
    /// Build the project through <paramref name="route"/> in <paramref name="configuration"/> and read
    /// <c>obj/gen</c> back. Fails the test when the route failed for any reason but the toolchain
    /// gate (BL6015): a transpile or codegen refusal would leave nothing worth comparing.
    ///
    /// <para><paramref name="configuration"/> may be <c>null</c> on the two routes that have a default
    /// of their own — the IDE service (never told a configuration: <c>Debug</c>) and the CLI (no
    /// <c>-c</c>: <c>Debug</c>); the in-process routes take it as <c>Debug</c>.</para>
    /// </summary>
    public CppProjectEmission Emit(CppProjectRoute route, string? configuration)
    {
        var named = configuration ?? "Debug";
        if (Directory.Exists(ObjGen)) Directory.Delete(ObjGen, recursive: true);
        string diagnostics, summary;
        switch (route)
        {
            case CppProjectRoute.BuildApi:
            {
                var r = CppProjectBuilder.Build(Load(), named,
                    resolveById: _ => null,
                    probeAvailability: () => default,
                    resolveToolchain: () => null);
                diagnostics = string.Join("\n", r.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
                summary = "BuildApi " + named;
                break;
            }
            case CppProjectRoute.IntelliSense:
            {
                var r = IntelliSenseEmitter.Emit(Load(), named, toolchain: null);
                diagnostics = string.Join("\n", r.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
                Assert.That(r.Success, Is.True, "IntelliSense emission failed:\n" + diagnostics);
                summary = "IntelliSense " + named;
                break;
            }
            case CppProjectRoute.IntelliSenseService:
            {
                using var service = new IntelliSenseEmissionService(new Mock<IOutputService>().Object, new CppToolchainOverrides(null));
                var project = new ProjectSerializer().LoadAsync(BlprojPath).GetAwaiter().GetResult();
                service.RequestEmit(project, named).GetAwaiter().GetResult();
                diagnostics = "";
                summary = "IntelliSenseService " + named;
                break;
            }
            case CppProjectRoute.IdeBuildService:
            {
                var service = new BuildService(new Mock<IOutputService>().Object, new ProjectSerializer(),
                    new CppToolchainOverrides(null), pathResolve: _ => null);
                if (configuration != null)
                    service.CurrentConfiguration = new CoreBuildConfiguration { Name = configuration };
                var project = new ProjectSerializer().LoadAsync(BlprojPath).GetAwaiter().GetResult();
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var r = service.BuildProjectAsync(project, cts.Token).GetAwaiter().GetResult();
                diagnostics = string.Join("\n", r.Diagnostics.Select(d => $"{d.Id}: {d.Message}"));
                summary = "IdeBuildService " + (configuration ?? "(default configuration)");
                break;
            }
            case CppProjectRoute.Cli:
            {
                var args = configuration == null
                    ? new[] { "build", BlprojPath }
                    : new[] { "build", BlprojPath, "-c", configuration };
                var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args, Dir, timeoutMs: 300_000);
                diagnostics = $"exit {exit}\n{stdout}\n{stderr}";
                summary = "Cli " + (configuration ?? "(no -c)");
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(route));
        }

        // Anything the route said that is NOT the toolchain gate means it never got as far as the
        // files this test reads (or read stale ones).
        var refusals = Regex.Matches(diagnostics, @"\bBL\d{4}\b").Select(m => m.Value).Distinct()
            .Where(code => code != "BL6015").ToList();
        var files = ReadObjGen();
        Assert.That(files, Is.Not.Empty, $"{summary}: nothing in obj/gen.\n{diagnostics}");
        Assert.That(files.Keys, Does.Contain("Main.g.cpp"), $"{summary}: no Main.g.cpp.\n{diagnostics}");
        Assert.That(refusals, Is.Empty, $"{summary}: refused for a reason other than the toolchain gate.\n{diagnostics}");
        return new CppProjectEmission(files, diagnostics, summary);
    }

    /// <summary>The first compile-database entry's arguments (<c>obj/compile_commands.json</c>).</summary>
    public List<string> CompileCommandArguments()
    {
        var path = Path.Combine(Dir, "obj", "compile_commands.json");
        Assert.That(File.Exists(path), Is.True, "no obj/compile_commands.json");
        var db = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        return db[0]!["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
    }
}

/// <summary>
/// ⭐ "WHICH PIPELINE" AS A LINE MULTISET. The in-process helpers (<c>BclE2E.CompileToCppOptimized</c>
/// = <c>AddStandardPasses</c>, <c>BclE2E.CompileToCppAggressive</c> = <c>AggressivePipeline.Apply</c>)
/// emit ONE combined translation unit with the runtime spliced in front; a project writes
/// per-module <c>.g.h</c>/<c>.g.cpp</c> files. The layout differs, the statements do not — so the
/// two are compared as multisets of trimmed code lines, after the runtime and the layout-only lines
/// (preprocessor lines, the banners, the D3 <c>inline</c>) are dropped. The expectation is DERIVED
/// from the pipelines (CLAUDE.md: validate through the optimizer, state the contract, not a frozen
/// string): "a Release project's code is the aggressive pipeline's code".
/// </summary>
internal static class CppPipelineLines
{
    private static readonly Regex RuntimeEnd = new(@"^#endif /\* BASICLANG_\w+ \*/[ \t]*$", RegexOptions.Multiline);

    /// <summary>The combined single-file output with the spliced runtime cut off the front.</summary>
    public static string AfterRuntime(string combined)
    {
        var ends = RuntimeEnd.Matches(combined);
        return ends.Count == 0 ? combined : combined[(ends[^1].Index + ends[^1].Length)..];
    }

    public static SortedDictionary<string, int> Multiset(string code)
    {
        var bag = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in code.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;                  // blank, preprocessor, #line
            if (line.StartsWith("// Function implementations")) continue;            // layout banners
            if (line.StartsWith("// Template function definitions")) continue;
            if (line.StartsWith("// Global variables")) line = "// Global variables";
            if (line.StartsWith("inline ")) line = line["inline ".Length..];          // D3: header-level definitions
            bag[line] = bag.GetValueOrDefault(line) + 1;
        }
        return bag;
    }

    /// <summary>Signed difference <paramref name="a"/> − <paramref name="b"/>; zero counts are omitted.</summary>
    public static SortedDictionary<string, int> Delta(SortedDictionary<string, int> a, SortedDictionary<string, int> b)
    {
        var d = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in a.Keys.Union(b.Keys))
        {
            var n = a.GetValueOrDefault(key) - b.GetValueOrDefault(key);
            if (n != 0) d[key] = n;
        }
        return d;
    }

    public static string Describe(SortedDictionary<string, int> bag) =>
        bag.Count == 0 ? "(none)" : string.Join("\n", bag.Select(kv => $"  {kv.Value:+0;-0} {kv.Key}"));

    /// <summary>The program's code as the STANDARD pipeline writes it, runtime and layout aside.</summary>
    public static SortedDictionary<string, int> Standard(string source) =>
        Multiset(AfterRuntime(BclE2E.CompileToCppOptimized(source)));

    /// <summary>The program's code as the AGGRESSIVE pipeline writes it, runtime and layout aside.</summary>
    public static SortedDictionary<string, int> Aggressive(string source) =>
        Multiset(AfterRuntime(BclE2E.CompileToCppAggressive(source)));

    /// <summary>The same view of a project's generated code.</summary>
    public static SortedDictionary<string, int> Of(CppProjectEmission emission) => Multiset(emission.UserCode);
}

/// <summary>The probe programs. See <see cref="CppPipelineProgram"/>.</summary>
internal static class CppPipelinePrograms
{
    /// <summary>algebraic simplification: `2 * x` becomes `x + x`, constants fold through a Long and a Double, a repeated `2 * x` is shared inside a loop. Output is vbc's.</summary>
    public static readonly CppPipelineProgram Alg = new("x_alg", """
        Class K
            Public P As Integer = 7
            Public Q As Long = 9
            Function Twice() As Integer
                Return 2 * P
            End Function
        End Class

        Function Seed(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Dim x As Integer = Seed(21)
            Dim l As Long = 3000000000
            Dim d As Double = 1.25
            Dim k As New K()
            Console.WriteLine(2 * x)
            Console.WriteLine(x * 2)
            Console.WriteLine(2 * l)
            Console.WriteLine(l * 2)
            Console.WriteLine(2 * d)
            Console.WriteLine(k.Twice())
            Console.WriteLine(2 * k.Q)
            Dim s As Integer = 0
            For i As Integer = 1 To 3
                s = s + 2 * i + 2 * x
            Next
            Console.WriteLine(s)
        End Sub
        """, "42\n42\n6000000000\n6000000000\n2.5\n14\n18\n138\n");

    /// <summary>LICM over a field product `A * B`: hoisted from RunPure's loop, NOT from Run's, whose loop calls Bump() (which writes A). Output is vbc's.</summary>
    public static readonly CppPipelineProgram Field = new("x_field", """
        Class Acc
            Public A As Integer
            Public B As Integer
            Sub Bump()
                A = A + 1
            End Sub
            Function Run(n As Integer) As Integer
                Dim s As Integer = 0
                For i As Integer = 1 To n
                    s = s + A * B
                    If i = 2 Then Bump()
                Next
                Return s
            End Function
            Function RunPure(n As Integer) As Integer
                Dim s As Integer = 0
                For i As Integer = 1 To n
                    s = s + A * B
                Next
                Return s
            End Function
        End Class

        Sub Main()
            Dim a As New Acc()
            a.A = 3
            a.B = 5
            Console.WriteLine(a.Run(4))
            Console.WriteLine(a.RunPure(4))
        End Sub
        """, "70\n80\n");

    /// <summary>LICM in nested counted loops and nested While loops, with an outer-loop write the hoist must respect. Output is vbc's.</summary>
    public static readonly CppPipelineProgram Nested = new("x_nested", """
        Function Seed(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Dim m As Integer = Seed(3)
            Dim s As Integer = 0
            For i As Integer = 1 To 3
                For j As Integer = 1 To 4
                    s = s + i * m + j
                Next
                m = m + 1
            Next
            Console.WriteLine(s)
            Dim t As Integer = 0
            Dim i2 As Integer = 0
            While i2 < 3
                Dim j2 As Integer = 0
                While j2 < 2
                    t = t + m * m
                    j2 = j2 + 1
                End While
                i2 = i2 + 1
            End While
            Console.WriteLine(t)
        End Sub
        """, "134\n216\n");

    /// <summary>the LICM control: a truly invariant `x * y` with no call in the loop, which must still hoist. Output is vbc's.</summary>
    public static readonly CppPipelineProgram InvariantProduct = new("d2_L6", """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim x As Integer = Seed(6)
            Dim y As Integer = Seed(7)
            Dim s As Integer = 0
            For i As Integer = 1 To 3
                s = s + x * y
            Next
            Console.WriteLine(CStr(s))
        End Sub
        """, "seed\nseed\n126\n");

    /// <summary>an invariant-looking `p + q` beside a call in the loop body. Output is vbc's.</summary>
    public static readonly CppPipelineProgram CallInLoop = new("d2_L7", """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim p As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim l As New List(Of Integer)()
            l.Add(0)
            For i As Integer = 1 To 2
                Dim a As Integer = p + q
                a = Seed(0)
                l(0) = p + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            Next
        End Sub
        """, "seed\nseed\nseed\n3,0\nseed\n3,0\n");

    /// <summary>zero-trip loops whose bodies could trap (a `10 \\ z` division by zero, a CInt overflow): nothing may be hoisted past the loop test. Output is vbc's.</summary>
    public static readonly CppPipelineProgram ZeroTrip = new("x_zero_trip", """
        Function Big() As Integer
            Return 100000
        End Function

        Sub Main()
            Dim bg As Integer = Big()
            Dim s As Integer = 0
            Dim n As Integer = 0
            For i As Integer = 1 To n
                s = s + bg * bg
            Next
            Console.WriteLine(s)
            Dim d As Double = 1E+20
            Dim k As Integer = 0
            While k < n
                s = s + CInt(d)
                k = k + 1
            End While
            Console.WriteLine(s)
            Dim z As Integer = 0
            Dim u As Integer = 0
            Do While u < n
                s = s + 10 \ z
                u = u + 1
            Loop
            Console.WriteLine(s)
        End Sub
        """, "0\n0\n0\n");

    /// <summary>an `Exit For` and a bottom-tested `Do ... Loop Until` around invariant products. Output is vbc's.</summary>
    public static readonly CppPipelineProgram ExitFor = new("x_exit", """
        Function Seed(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Dim a As Integer = Seed(3)
            Dim b As Integer = Seed(8)
            Dim s As Integer = 0
            For i As Integer = 1 To 10
                If s > 50 Then
                    Exit For
                End If
                s = s + a * b
            Next
            Console.WriteLine(s)
            Dim k As Integer = 0
            Do
                k = k + 1
                s = s + (a + b) * 2
                If k <> 2 Then
                    s = s + 1
                End If
            Loop Until k >= 4
            Console.WriteLine(s)
        End Sub
        """, "72\n163\n");

    /// <summary>Double arithmetic in loops: invariant `a * b + a / b` and `(a - b) * 2`. Output is vbc's.</summary>
    public static readonly CppPipelineProgram DoubleLoop = new("x_double", """
        Function Seed(v As Double) As Double
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim a As Double = Seed(1.5)
            Dim b As Double = Seed(2.25)
            Dim s As Double = 0
            For i As Integer = 1 To 4
                s = s + a * b + a / b
            Next
            Console.WriteLine(s)
            Dim t As Double = 0
            Dim k As Integer = 0
            While k < 3
                t = t + (a - b) * 2
                k = k + 1
            End While
            Console.WriteLine(t)
        End Sub
        """, "seed\nseed\n16.166666666666668\n-4.5\n");

    /// <summary>Programs whose generated C++ the two pipelines write differently (asserted, not assumed).</summary>
    public static IEnumerable<TestCaseData> Discriminating() =>
        new[] { Alg, Field, Nested, InvariantProduct, CallInLoop, ZeroTrip, ExitFor, DoubleLoop }
            .Select(p => new TestCaseData(p).SetArgDisplayNames(p.Name));

    /// <summary>The programs the execution tier builds, compiles with clang and runs.</summary>
    public static IEnumerable<TestCaseData> Runnable() =>
        new[] { Alg, Field, Nested, InvariantProduct, CallInLoop, ZeroTrip, ExitFor, DoubleLoop }
            .Select(p => new TestCaseData(p).SetArgDisplayNames(p.Name));
}
