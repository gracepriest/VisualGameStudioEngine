using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BasicLang.Compiler.Driver;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 25 through the CLI — BOTH entry points, which CLAUDE.md requires explicitly.
///
/// <para><b>The project route carried a real bug these tests pin.</b> It had its own private
/// copy of the backend switch whose <c>default:</c> was C#, so
/// <c>build proj.blproj</c> on a <c>&lt;TargetBackend&gt;JavaScript&lt;/TargetBackend&gt;</c>
/// project wrote a <c>.cs</c> file, ran <c>dotnet build</c> on it, printed "Build succeeded"
/// and exited 0. The single-file route had been hardened against exactly this three weeks
/// earlier — its comment says emitting a different language than the one asked for is "the
/// worst kind of quiet wrongness: the build succeeds" — but the second copy of the decision
/// never got the fix.</para>
/// </summary>
[TestFixture]
public class JavaScriptCliSiteTests
{
    private string _dir;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "BasicLang_JsSite_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* a locked temp dir must not fail a passing test */ }
    }

    private const string Source = "Sub Main()\nConsole.WriteLine(1)\nEnd Sub\n";

    /// <summary>Runs the CLI with console output captured, so a build banner cannot pollute the run log.</summary>
    private static async Task<(int exit, string output)> Cli(params string[] args)
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            Console.SetError(captured);
            var exit = await Program.Main(args);
            return (exit, captured.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    // ---------------------------------------------------------------- single-file route

    [Test]
    [NonParallelizable]
    public async Task SingleFile_EmitsScriptAndHarness()
    {
        var bas = Path.Combine(_dir, "prog.bas");
        File.WriteAllText(bas, Source);

        var (exit, _) = await Cli(bas, "--target=javascript");

        Assert.That(exit, Is.Zero);
        Assert.That(File.Exists(Path.Combine(_dir, "prog.js")), Is.True, "the script");
        Assert.That(File.Exists(Path.Combine(_dir, "index.html")), Is.True, "the harness");
        Assert.That(File.ReadAllText(Path.Combine(_dir, "index.html")),
            Does.Contain("src=\"prog.js\""));
    }

    /// <summary>
    /// The single-file route writes NEXT TO THE SOURCE — the same folder a hand-authored
    /// index.html lives in. Clobbering it would destroy the user's page on every build.
    /// </summary>
    [Test]
    [NonParallelizable]
    public async Task SingleFile_DoesNotClobberAHandWrittenHarness()
    {
        File.WriteAllText(Path.Combine(_dir, "prog.bas"), Source);
        var mine = "<!-- mine -->";
        File.WriteAllText(Path.Combine(_dir, "index.html"), mine);

        await Cli(Path.Combine(_dir, "prog.bas"), "--target=javascript");

        Assert.That(File.ReadAllText(Path.Combine(_dir, "index.html")), Is.EqualTo(mine));
    }

    /// <summary>A non-JS target must not start littering harnesses into source folders.</summary>
    [Test]
    [NonParallelizable]
    public async Task SingleFile_CSharpTarget_EmitsNoHarness()
    {
        File.WriteAllText(Path.Combine(_dir, "prog.bas"), Source);

        await Cli(Path.Combine(_dir, "prog.bas"), "--target=csharp");

        Assert.That(File.Exists(Path.Combine(_dir, "index.html")), Is.False);
    }

    // ---------------------------------------------------------------- project route

    private string WriteProject(string backend)
    {
        File.WriteAllText(Path.Combine(_dir, "Main.bas"), Source);
        var proj = Path.Combine(_dir, "Site.blproj");
        File.WriteAllText(proj,
            "<Project>\n" +
            "  <PropertyGroup>\n" +
            "    <ProjectName>Site</ProjectName>\n" +
            "    <AssemblyName>Site</AssemblyName>\n" +
            $"    <TargetBackend>{backend}</TargetBackend>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <Compile Include=\"Main.bas\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");
        return proj;
    }

    /// <summary>Finds the built file regardless of which bin/&lt;config&gt;/&lt;tfm&gt; shape the route chose.</summary>
    private string[] Built(string pattern) =>
        Directory.Exists(Path.Combine(_dir, "bin"))
            ? Directory.GetFiles(Path.Combine(_dir, "bin"), pattern, SearchOption.AllDirectories)
            : Array.Empty<string>();

    /// <summary>THE REGRESSION TEST. This project used to build a .cs file and report success.</summary>
    [Test]
    [NonParallelizable]
    public async Task ProjectRoute_JavaScript_EmitsJsNotCs()
    {
        var (exit, output) = await Cli("build", WriteProject("JavaScript"));

        Assert.That(exit, Is.Zero, output);
        Assert.That(Built("*.js"), Is.Not.Empty, "a .js must be emitted");
        Assert.That(Built("*.cs"), Is.Empty, "a JavaScript project must never emit C#");
    }

    [Test]
    [NonParallelizable]
    public async Task ProjectRoute_JavaScript_EmitsTheHarnessBesideTheScript()
    {
        await Cli("build", WriteProject("JavaScript"));

        var script = Built("*.js");
        Assert.That(script, Is.Not.Empty);

        var harness = Path.Combine(Path.GetDirectoryName(script[0])!, "index.html");
        Assert.That(File.Exists(harness), Is.True);
        Assert.That(File.ReadAllText(harness),
            Does.Contain("src=\"" + Path.GetFileName(script[0]) + "\""));
    }

    /// <summary>
    /// The other half of the same bug: an unrecognised backend used to fall through to C#.
    /// It must now fail the build rather than quietly emit the wrong language.
    /// </summary>
    [Test]
    [NonParallelizable]
    public async Task ProjectRoute_UnknownBackend_FailsInsteadOfEmittingCSharp()
    {
        var (exit, output) = await Cli("build", WriteProject("jscript"));

        Assert.That(exit, Is.Not.Zero, "an unknown backend must fail the build");
        Assert.That(output, Does.Contain("jscript"));
        Assert.That(Built("*.cs"), Is.Empty, "must not silently emit C#");
    }

    // ---------------------------------------------------------------- source maps (task 26)

    [Test]
    [NonParallelizable]
    public async Task SingleFile_EmitsASourceMapAndLinksIt()
    {
        File.WriteAllText(Path.Combine(_dir, "prog.bas"), Source);

        await Cli(Path.Combine(_dir, "prog.bas"), "--target=javascript");

        var mapPath = Path.Combine(_dir, "prog.js.map");
        Assert.That(File.Exists(mapPath), Is.True, "a .map must be emitted");
        Assert.That(File.ReadAllText(Path.Combine(_dir, "prog.js")).TrimEnd(),
            Does.EndWith("//# sourceMappingURL=prog.js.map"));

        var map = JsonDocument.Parse(File.ReadAllText(mapPath)).RootElement;
        Assert.That(map.GetProperty("version").GetInt32(), Is.EqualTo(3));
        Assert.That(map.GetProperty("sources")[0].GetString(), Is.EqualTo("prog.bas"));
        Assert.That(map.GetProperty("mappings").GetString(), Is.Not.Empty);
    }

    /// <summary>
    /// The project route emits into bin/… while the .bas stays in the project root, so a
    /// browser resolving `sources` against the map's URL would 404 on every one. Inlined
    /// text is what makes the map usable there.
    /// </summary>
    [Test]
    [NonParallelizable]
    public async Task ProjectRoute_InlinesSourceContentIntoTheMap()
    {
        await Cli("build", WriteProject("JavaScript"));

        var maps = Built("*.js.map");
        Assert.That(maps, Is.Not.Empty, "a .map must be emitted");

        var map = JsonDocument.Parse(File.ReadAllText(maps[0])).RootElement;
        var contents = map.GetProperty("sourcesContent");
        Assert.That(contents.GetArrayLength(), Is.EqualTo(map.GetProperty("sources").GetArrayLength()));
        Assert.That(contents[0].GetString(), Does.Contain("Console.WriteLine"));
    }

    /// <summary>
    /// THE OFF-BY-ONE. A `.mod` is wrapped in an implicit Module block before parsing, so
    /// every IR line for it is one greater than the line the user sees. The correction lived
    /// on CompilationUnit where no backend could reach it — mapping the raw value is correct
    /// for `.bas` and wrong for `.mod`, so the obvious test would never catch it.
    /// </summary>
    [Test]
    [NonParallelizable]
    public async Task ModFile_MapsToTheLineTheUserWrote()
    {
        // No Module wrapper in the text — a .mod gets one inserted, shifting every line by 1.
        var mod = Path.Combine(_dir, "Helpers.mod");
        File.WriteAllText(mod, "Sub Main()\nConsole.WriteLine(1)\nEnd Sub\n");

        var (exit, output) = await Cli(mod, "--target=javascript");
        Assert.That(exit, Is.Zero, output);

        var map = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "Helpers.js.map"))).RootElement;
        var mappings = map.GetProperty("mappings").GetString() ?? "";

        // Decode absolute source lines; the WriteLine is on the user's line 2 => 0-based 1.
        var lines = new List<int>();
        var current = 0;
        foreach (var group in mappings.Split(';'))
        {
            if (group.Length == 0) continue;
            foreach (var seg in group.Split(','))
            {
                var f = BasicLang.Compiler.CodeGen.JavaScript.JavaScriptSourceMap.DecodeVlq(seg);
                if (f.Count < 4) continue;
                current += f[2];
                lines.Add(current);
            }
        }

        Assert.That(lines, Is.Not.Empty, "the .mod must produce mappings");
        Assert.That(lines, Has.All.LessThan(3),
            "a 3-line .mod cannot map past its own last line — a wrapper offset leaked through");
        Assert.That(lines, Does.Contain(1),
            "Console.WriteLine is on the user's line 2 (0-based 1), not the wrapped line 3");
    }
}
