using System.Collections.Concurrent;
using System.Diagnostics;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Core.Utilities;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// Task 13 (C++ Phase 3a): the END-TO-END proof. Every prior task in this phase is unit-level
/// or launch-level; this fixture is the only place the WHOLE chain is exercised against real
/// servers: BasicLang → C++ header emission (<see cref="IntelliSenseEmitter"/>) → clangd
/// launched per project with <c>--compile-commands-dir</c> → real IntelliSense ANSWERS in a
/// mixed BasicLang+C++ project.
///
/// <para>The five plan assertions, one test each:</para>
/// <list type="number">
/// <item><description>completion at a <c>::</c> on a std type returns non-empty
/// (<see cref="Completion_AfterStdScope_ReturnsStdMembers"/>);</description></item>
/// <item><description>hover on a symbol returns non-empty — and the symbol chosen is a
/// BasicLang-defined function, so the hover doubles as Direction-B proof
/// (<see cref="Hover_OnBasicLangFunction_DescribesIt"/>);</description></item>
/// <item><description>diagnostics for a deliberate syntax error arrive AND land in
/// <see cref="DiagnosticsAggregator"/> through the same event shape
/// <c>MainWindowViewModel.OnDiagnosticsReceived</c> consumes
/// (<see cref="Diagnostics_ForDeliberateSyntaxError_ArriveAndLandInTheAggregator"/>);</description></item>
/// <item><description>go-to-definition on a BasicLang symbol lands in the generated
/// <c>obj/gen/*.g.h</c> — Direction B interop plus the compile-database include path,
/// in one assertion (<see cref="GoToDefinition_OnBasicLangClass_LandsInTheGeneratedHeader"/>);</description></item>
/// <item><description><c>.bas</c> files in the SAME project are still answered by the
/// BasicLang server — routing identity AND a live BasicLang-shaped answer
/// (<see cref="BasFile_IsAnsweredByTheBasicLangServer_NoCrossTalk"/>).</description></item>
/// </list>
///
/// <para>
/// <b>One fixture, one server pair.</b> The chain under test — emit, then
/// <see cref="LanguageServiceRegistry.StartAllAsync"/> (the production project-open seam), then
/// didOpen — is built once in <see cref="OneTimeSetUp"/> and shared, because starting clangd and
/// a <c>dotnet --lsp</c> child per assertion would quintuple the slowest part of the suite for
/// no isolation gain: every test here only READS server state established at setup.
/// </para>
///
/// <para>
/// <b>Skips when clangd is not installed</b>, resolving through the identical probe
/// <see cref="ClangdLaunchTests"/> uses (the <c>cpp.clangd.path</c>-override branch against
/// <c>~\.vgs\tools\clangd*\bin\clangd.exe</c>, then PATH). On the phase's dev machine clangd IS
/// installed and these must RUN — a skip there is a task failure, not a pass.
/// </para>
///
/// <para>
/// Emission passes <c>toolchain: null</c> explicitly, mirroring the project-open path's D2
/// decision — never the machine's real toolchain, so the compile database is the same
/// clang++-driver one the IDE would produce on a toolchain-less machine.
/// </para>
/// </summary>
[TestFixture]
[Category("RequiresClangd")]
[NonParallelizable] // spawns clangd + `dotnet BasicLang.dll --lsp` child processes
public class ClangdE2ETests
{
    // ------------------------------------------------------------------
    // The mixed project. Positions asserted against are documented per line
    // (LanguageService's line/column parameters are 1-based; it converts).
    // ------------------------------------------------------------------

    private const string ProjectName = "MixedIse";

    private const string MixedBlproj = $"""
        <BasicLangProject Version="1.0">
          <PropertyGroup>
            <ProjectName>{ProjectName}</ProjectName>
            <OutputType>Exe</OutputType>
            <Language>Cpp</Language>
            <TargetBackend>Cpp</TargetBackend>
          </PropertyGroup>
        </BasicLangProject>
        """;

    /// <summary>
    /// The BasicLang side: a class and a free function, both of which Direction B exposes to
    /// user C++ through the generated headers (the class definition lives in the aggregate
    /// <c>MixedIse.g.h</c>; the per-module shim <c>Logic.g.h</c> includes it).
    /// </summary>
    private const string LogicBas =
        "Class Player\n" +
        "    Public Name As String\n" +
        "    Function Tag() As String\n" +
        "        Return Name\n" +
        "    End Function\n" +
        "End Class\n" +
        "\n" +
        "Function CalculateScore(hits As Integer) As Integer\n" +
        "    Return hits * 10\n" +                       // line 9, col 5 — BasicLang completion
        "End Function\n";

    /// <summary>
    /// The user C++ side. Line/column map (1-based, matching the calls below):
    /// line 5 col 12 = after <c>std::st</c> (scope completion on a std type);
    /// line 6 col 20 = inside <c>CalculateScore</c> (hover);
    /// line 7 col 6  = inside <c>Player</c> (go-to-definition).
    /// </summary>
    private const string MainCpp =
        "#include \"Logic.g.h\"\n" +                     // line 1 — resolves ONLY via obj/gen -I
        "#include <string>\n" +                          // line 2
        "\n" +                                           // line 3
        "int main() {\n" +                               // line 4
        "    std::string message = \"hi\";\n" +          // line 5
        "    int score = CalculateScore(5);\n" +         // line 6
        "    Player* who = nullptr;\n" +                 // line 7
        "    (void)message;\n" +                         // line 8
        "    (void)who;\n" +                             // line 9
        "    return score == 50 ? 0 : 1;\n" +            // line 10
        "}\n";                                           // line 11

    /// <summary>A deliberate SYNTAX error (missing ';'), on line 1, for assertion 3.</summary>
    private const string BrokenCpp = "int answer() { return 42 }\n";

    // ------------------------------------------------------------------
    // Timing constants
    // ------------------------------------------------------------------

    /// <summary>
    /// Bound on every polled server answer. Generous by design: clangd answered the launch
    /// tests in ~150ms on this hardware, but parsing/indexing a real TU (and BasicLang's
    /// post-didOpen analysis) takes seconds — this is a ceiling for CI-grade slowness, never
    /// an expectation.
    /// </summary>
    private static readonly TimeSpan ServerAnswerTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Bound on the race test's edit-probe healing loop — roughly 6-9× the 5-7s the heal
    /// measured on this hardware (clangd's ~5s CDB freshness window plus one rebuild).
    /// </summary>
    private static readonly TimeSpan HealBudget = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Gap between edit-probes in the healing loop: ≈7 probes crosses clangd's ~5s CDB
    /// freshness window, so the window is straddled well within <see cref="HealBudget"/>.
    /// </summary>
    private static readonly TimeSpan EditProbeCadence = TimeSpan.FromMilliseconds(700);

    // ------------------------------------------------------------------
    // Fixture state
    // ------------------------------------------------------------------

    private string _projectDir = "";
    private string _mainCpp = "", _brokenCpp = "", _logicBas = "";
    private LanguageService _clangd = null!;
    private LanguageService _basicLang = null!;
    private LanguageServiceRegistry _registry = null!;
    private RecordingOutput _output = null!;

    /// <summary>The Error List store, fed exactly the way the shell feeds it (see handler).</summary>
    private readonly DiagnosticsAggregator _aggregator = new();

    private readonly object _publishLock = new();
    private readonly List<(string FilePath, IReadOnlyList<DiagnosticItem> Diagnostics)> _clangdPublishes = new();

    // ------------------------------------------------------------------
    // clangd discovery — same probe as ClangdLaunchTests: the ~\.vgs\tools dir is passed as
    // the configuredPath override (exercising the cpp.clangd.path branch against a real
    // executable on a machine that deliberately keeps clangd off PATH), then PATH.
    // ------------------------------------------------------------------

    private static string? LocateClangd() =>
        ClangdLocator.ResolveClangdPath(configuredPath: ProbeVgsToolsDir());

    private static string? ProbeVgsToolsDir()
    {
        try
        {
            var toolsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vgs", "tools");
            if (!Directory.Exists(toolsDir)) return null;

            return Directory.GetDirectories(toolsDir, "clangd*")
                .OrderByDescending(dir => dir, StringComparer.OrdinalIgnoreCase)
                .Select(dir => Path.Combine(dir, "bin", "clangd.exe"))
                .FirstOrDefault(File.Exists);
        }
        catch
        {
            return null; // an unreadable profile dir degrades to the PATH probe, never fails the fixture
        }
    }

    private static void RequireClangd(string? clangdPath)
    {
        if (clangdPath == null)
        {
            Assert.Ignore(
                "clangd is not installed on this machine (not under ~\\.vgs\\tools\\clangd*\\bin, " +
                "not on PATH). This fixture drives a real clangd end-to-end; install clangd to run it.");
        }
    }

    // ------------------------------------------------------------------
    // Setup / teardown
    // ------------------------------------------------------------------

    /// <summary>
    /// Emission, then servers, then didOpen — deliberately in that order: headers and the
    /// compile database are on disk before clangd starts. The shell's project-open fires
    /// emission and server-start as independent fire-and-forgets and CANNOT guarantee this
    /// ordering; the late-database ordering the shell can produce is pinned separately in
    /// <see cref="DidOpen_BeforeTheCompileDbExists_HealsThroughSubsequentEdits_OnceItAppears"/>.
    /// </summary>
    [OneTimeSetUp]
    public async Task EmitStartAndOpen()
    {
        var clangdPath = LocateClangd();
        RequireClangd(clangdPath);

        _projectDir = Path.Combine(Path.GetTempPath(), "bl-clangd-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
        _output = new RecordingOutput();

        var blprojPath = Path.Combine(_projectDir, "App.blproj");
        File.WriteAllText(blprojPath, MixedBlproj);
        _logicBas = WriteFile("Logic.bas", LogicBas);
        _mainCpp = WriteFile("main.cpp", MainCpp);
        _brokenCpp = WriteFile("broken.cpp", BrokenCpp);

        // EMIT FIRST — the deterministic ordering; see the method doc for why, and for the
        // pointer to where the late-database ordering is pinned. The doc's cref is
        // IDE-checked only (this project emits no XML doc file, so `dotnet build` skips
        // cref validation — probed empirically with a bogus cref: no CS1574); the nameof
        // below is what actually breaks the BUILD if the race test is ever renamed.
        _ = nameof(DidOpen_BeforeTheCompileDbExists_HealsThroughSubsequentEdits_OnceItAppears);
        var emit = IntelliSenseEmitter.Emit(ProjectFile.Load(blprojPath), "Debug", toolchain: null);
        Assert.That(emit.Success, Is.True, "emission precondition failed: " + DiagCodes(emit));
        Assert.That(File.Exists(ObjGen(ProjectName + ".g.h")), Is.True,
            "the aggregate header (where the Player class definition lives) must exist");
        Assert.That(File.Exists(ObjGen("Logic.g.h")), Is.True,
            "the per-module shim main.cpp includes must exist");
        Assert.That(File.Exists(Path.Combine(_projectDir, "obj", "compile_commands.json")), Is.True);

        // The BasicLang service uses the DEFAULT descriptor — the same auto-probe the IDE runs,
        // which finds the BasicLang.dll deployed next to the test assembly.
        _basicLang = new LanguageService(_output);
        _clangd = new LanguageService(_output, null, LanguageServerDescriptor.Clangd(clangdPath!));

        // Subscribed BEFORE any didOpen so no publish can be missed.
        _clangd.DiagnosticsReceived += OnClangdDiagnostics;

        // The production project-open seam: one registry holding both servers, started at the
        // project root. This is what ProjectOpened → StartAllAsync does in the shell.
        _registry = new LanguageServiceRegistry(new ILanguageService[] { _basicLang, _clangd });
        await _registry.StartAllAsync(_projectDir);

        Assert.That(_clangd.IsConnected, Is.True, "clangd did not connect.\n" + _output.Dump());
        Assert.That(_basicLang.IsConnected, Is.True, "the BasicLang server did not connect.\n" + _output.Dump());

        await _clangd.OpenDocumentAsync(_mainCpp, MainCpp);
        await _clangd.OpenDocumentAsync(_brokenCpp, BrokenCpp);
        await _basicLang.OpenDocumentAsync(_logicBas, LogicBas);
    }

    [OneTimeTearDown]
    public async Task StopServersAndCleanUp()
    {
        if (_clangd != null) _clangd.DiagnosticsReceived -= OnClangdDiagnostics;

        // Stop through the registry when it exists (it owns both servers' lifecycle) …
        if (_registry != null)
        {
            try { await _registry.StopAllAsync(); } catch { /* teardown is best-effort */ }
            try { _registry.Dispose(); } catch { }
        }
        // … and dispose directly too, covering a setup that failed before the registry was built.
        try { _clangd?.Dispose(); } catch { }
        try { _basicLang?.Dispose(); } catch { }

        // Hygiene before verdicts elsewhere: NO child process may outlive the fixture, whether
        // the tests passed or not. ServerProcessId is retained past Stop/Dispose for exactly this.
        KillIfAlive(_clangd?.ServerProcessId);
        KillIfAlive(_basicLang?.ServerProcessId);

        if (_projectDir.Length > 0)
        {
            // One bounded retry: the just-stopped clangd can briefly hold the project dir open.
            try { Directory.Delete(_projectDir, recursive: true); }
            catch
            {
                Thread.Sleep(250);
                try { Directory.Delete(_projectDir, recursive: true); } catch { }
            }
        }
    }

    // ------------------------------------------------------------------
    // 1. Completion at a :: on a std type
    // ------------------------------------------------------------------

    [Test]
    public async Task Completion_AfterStdScope_ReturnsStdMembers()
    {
        // Line 5, col 12 = after "std::st" — scope completion into namespace std, filtered by
        // "st". clangd parses the TU asynchronously after didOpen, so poll rather than demand
        // an instant answer.
        var completions = await PollUntilAsync(
            () => _clangd.GetCompletionsAsync(_mainCpp, line: 5, column: 12),
            c => c.Count > 0,
            ServerAnswerTimeout,
            "clangd completion after 'std::st' in main.cpp",
            _output.Dump);

        Assert.That(completions, Is.Not.Empty);
        Assert.That(completions.Any(c => c.Label.Trim().StartsWith("string", StringComparison.Ordinal)),
            Is.True,
            "completion after 'std::st' must offer std::string — a std member, proving system " +
            "includes and the compile database reached clangd. Got: " + Labels(completions));
    }

    // ------------------------------------------------------------------
    // 2. Hover on a symbol (a BasicLang-defined one, deliberately)
    // ------------------------------------------------------------------

    [Test]
    public async Task Hover_OnBasicLangFunction_DescribesIt()
    {
        // Line 6, col 20 = inside "CalculateScore" — a function that exists ONLY because the
        // emission seam transpiled Logic.bas, so a non-empty hover is cross-language interop
        // answering, not just clangd answering.
        var hover = await PollUntilAsync(
            () => _clangd.GetHoverAsync(_mainCpp, line: 6, column: 20),
            h => h != null,
            ServerAnswerTimeout,
            "clangd hover over the CalculateScore call in main.cpp",
            _output.Dump);

        Assert.That(hover!.Contents, Is.Not.Empty, "hover arrived but carried no contents");
        Assert.That(hover.Contents, Does.Contain("CalculateScore"),
            $"hover must describe the BasicLang-defined function; got: '{hover.Contents}'");
    }

    // ------------------------------------------------------------------
    // 3. Diagnostics for a deliberate syntax error → DiagnosticsAggregator
    // ------------------------------------------------------------------

    [Test]
    public async Task Diagnostics_ForDeliberateSyntaxError_ArriveAndLandInTheAggregator()
    {
        // (a) The raw publish: clangd must report the missing ';' in broken.cpp as an ERROR on
        // line 1 (ProcessDiagnostics converts LSP's 0-based line to 1-based).
        var publish = await PollUntilAsync(
            () => Task.FromResult(LatestPublishFor(_brokenCpp)),
            p => p != null && p.Value.Diagnostics.Count > 0,
            ServerAnswerTimeout,
            "clangd publishDiagnostics for broken.cpp",
            _output.Dump);

        var errors = publish!.Value.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.That(errors, Is.Not.Empty,
            "the deliberate syntax error must surface as an ERROR: " + Describe(publish.Value.Diagnostics));
        Assert.That(errors[0].Line, Is.EqualTo(1),
            "the missing ';' is on line 1 of broken.cpp: " + Describe(errors));

        // (b) The aggregator: fed by OnClangdDiagnostics exactly the way
        // MainWindowViewModel.OnDiagnosticsReceived feeds the shell's instance, so this asserts
        // the production event shape reaches the Error List store — not a reinvented wiring.
        var forBroken = _aggregator.GetSnapshot()
            .Where(d => string.Equals(d.FilePath, _brokenCpp, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.That(forBroken.Any(d => d.Severity == DiagnosticSeverity.Error), Is.True,
            "clangd's error must land in DiagnosticsAggregator under broken.cpp's path; snapshot: " +
            Describe(_aggregator.GetSnapshot()));

        // (c) And the healthy TU must NOT be reporting an include failure — if the compile
        // database's -I obj/gen never reached clangd, main.cpp would carry
        // "'Logic.g.h' file not found" and every other test here would be answering from a
        // half-broken AST.
        var mainPublish = await PollUntilAsync(
            () => Task.FromResult(LatestPublishFor(_mainCpp)),
            p => p != null,
            ServerAnswerTimeout,
            "clangd publishDiagnostics for main.cpp",
            _output.Dump);
        Assert.That(mainPublish!.Value.Diagnostics.Any(
                d => d.Message.Contains("file not found", StringComparison.OrdinalIgnoreCase)),
            Is.False,
            "main.cpp must not fail its includes — the compile database's include path did not " +
            "reach clangd: " + Describe(mainPublish.Value.Diagnostics));
    }

    // ------------------------------------------------------------------
    // 4. Go-to-definition on a BasicLang symbol → the generated header
    // ------------------------------------------------------------------

    [Test]
    public async Task GoToDefinition_OnBasicLangClass_LandsInTheGeneratedHeader()
    {
        // Line 7, col 6 = inside "Player" — a TYPE, deliberately, not the free function: a class
        // DEFINITION lives in the generated header, so the target is deterministic. (The
        // function's definition lives in obj/gen/Logic.g.cpp, and clangd may answer with either
        // the declaration or, once its background index catches up, the definition — a race no
        // assertion should sit on.)
        var location = await PollUntilAsync(
            () => _clangd.GetDefinitionAsync(_mainCpp, line: 7, column: 6),
            l => l != null,
            ServerAnswerTimeout,
            "clangd textDocument/definition on 'Player' in main.cpp",
            _output.Dump);

        // Exact identity, not a substring grope: the resolved path must sit UNDER obj\gen and
        // BE a .g.h — this single assertion is what proves the Direction-B include path.
        var fullPath = Path.GetFullPath(location!.Uri);
        var objGenPrefix = Path.GetFullPath(Path.Combine(_projectDir, "obj", "gen"))
                           + Path.DirectorySeparatorChar;
        Assert.That(fullPath, Does.StartWith(objGenPrefix).IgnoreCase,
            "definition of a BasicLang symbol must resolve into the generated obj\\gen directory");
        Assert.That(fullPath, Does.EndWith(".g.h").IgnoreCase,
            "the class definition lives in a generated header, never a .g.cpp");
        Assert.That(location.Line, Is.GreaterThan(0), "the location must carry a real line");

        var definitionLine = File.ReadLines(fullPath).Skip(location.Line - 1).FirstOrDefault() ?? "";
        Assert.That(definitionLine, Does.Contain("Player"),
            $"the located line must be Player's definition; {fullPath}({location.Line}) reads: '{definitionLine}'");
    }

    // ------------------------------------------------------------------
    // 5. .bas in the same project → still the BasicLang server, no cross-talk
    // ------------------------------------------------------------------

    [Test]
    public async Task BasFile_IsAnsweredByTheBasicLangServer_NoCrossTalk()
    {
        // Routing identity through the same registry the shell routes with.
        Assert.Multiple(() =>
        {
            Assert.That(_registry.GetFor(_logicBas), Is.SameAs(_basicLang),
                ".bas must route to the BasicLang service");
            Assert.That(_registry.GetFor(_mainCpp), Is.SameAs(_clangd),
                ".cpp must route to the clangd service");
            Assert.That(_registry.GetFor(_brokenCpp), Is.SameAs(_clangd));
            Assert.That(_clangd.Descriptor.Owns(_logicBas), Is.False,
                "clangd must not claim the .bas file");
            Assert.That(_basicLang.Descriptor.Owns(_mainCpp), Is.False,
                "BasicLang must not claim the .cpp file");
        });

        // And a LIVE answer from the BasicLang server for the very same project's .bas —
        // line 9 col 5 is a statement position inside CalculateScore's body.
        var completions = await PollUntilAsync(
            () => _basicLang.GetCompletionsAsync(_logicBas, line: 9, column: 5),
            c => c.Count > 0,
            ServerAnswerTimeout,
            "BasicLang completion inside CalculateScore's body in Logic.bas",
            _output.Dump);

        Assert.That(completions.Any(c => c.Label == "Dim"), Is.True,
            "the answer must be BasicLang-shaped (the 'Dim' keyword — something clangd could " +
            "never produce), proving no cross-talk. Got: " + Labels(completions));
    }

    // ------------------------------------------------------------------
    // The launch-race observation from Task 12's review: OnProjectOpened fires emission and
    // server-start as independent fire-and-forgets, so clangd can come up BEFORE
    // obj/compile_commands.json is written. The claim on record was "clangd re-probes the db
    // on didOpen, so it should self-heal" — HALF-verified here, on this test's own server
    // instance so the shared fixture's deterministic emit-before-start ordering stays intact:
    //
    //   • A LONE didClose/didOpen right after the db appears is NOT enough (measured against
    //     real clangd 22: its directory-CDB cache trusts entries younger than ~5s without
    //     re-statting, and read-only queries like definition never consult the CDB at all —
    //     only document UPDATES fetch a compile command).
    //   • What DOES heal the session is exactly what a live editor produces anyway:
    //     subsequent didChanges. Once the cache's freshness window lapses, the next update
    //     re-stats the db, sees the new content, and rebuilds with the real flags.
    //
    // So the race degrades the FIRST parse only, and ordinary typing repairs it — pinned
    // below with bounded edit-probes; if clangd ever stopped reloading across edits, this
    // fails and the fire-and-forget race becomes a real product bug.
    // ------------------------------------------------------------------

    [Test]
    public async Task DidOpen_BeforeTheCompileDbExists_HealsThroughSubsequentEdits_OnceItAppears()
    {
        var clangdPath = LocateClangd();
        RequireClangd(clangdPath);

        var dir = Path.Combine(Path.GetTempPath(), "bl-clangd-e2e-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        LanguageService? service = null;

        try
        {
            var blprojPath = Path.Combine(dir, "App.blproj");
            File.WriteAllText(blprojPath, MixedBlproj);
            File.WriteAllText(Path.Combine(dir, "Logic.bas"), LogicBas);
            var mainCpp = Path.Combine(dir, "main.cpp");
            File.WriteAllText(mainCpp, MainCpp);

            // Emit for real (headers must exist), then REPLACE the database with the empty one
            // the launch tests use — the exact on-disk state of "clangd won the race".
            var emit = IntelliSenseEmitter.Emit(ProjectFile.Load(blprojPath), "Debug", toolchain: null);
            Assert.That(emit.Success, Is.True, "emission precondition failed: " + DiagCodes(emit));
            var dbPath = Path.Combine(dir, "obj", "compile_commands.json");
            var realDb = File.ReadAllText(dbPath);
            File.WriteAllText(dbPath, "[]");

            var publishes = new List<(string FilePath, IReadOnlyList<DiagnosticItem> Diagnostics)>();
            var publishesLock = new object();

            // Its OWN traffic log: NUnit's alphabetical order interleaves this test between
            // the shared-fixture tests, and borrowing _output would mix two servers' worlds
            // into one dump.
            var raceOutput = new RecordingOutput();
            service = new LanguageService(raceOutput, null,
                LanguageServerDescriptor.Clangd(clangdPath!));
            service.DiagnosticsReceived += (_, e) =>
            {
                if (e?.Diagnostics == null) return;
                var filePath = LanguageService.UriToPath(e.Uri ?? "");
                if (filePath.Length == 0) return;
                lock (publishesLock) { publishes.Add((filePath, e.Diagnostics)); }
            };

            await service.StartAsync(dir);
            Assert.That(service.IsConnected, Is.True, "clangd did not connect for the race variant");

            await service.OpenDocumentAsync(mainCpp, MainCpp);

            // Premise pinned first: under fallback flags (empty db) the generated include CANNOT
            // resolve — obj/gen is only ever on the include path via the compile database. If
            // this ever passes without the error, the premise is gone and the test means nothing.
            await PollUntilAsync(
                () => Task.FromResult(LatestFor(publishes, publishesLock, mainCpp)),
                p => p != null && p.Value.Diagnostics.Any(
                    d => d.Message.Contains("file not found", StringComparison.OrdinalIgnoreCase)),
                ServerAnswerTimeout,
                "the 'Logic.g.h' file-not-found diagnostic under fallback flags (empty compile db)",
                raceOutput.Dump);

            // The database appears late — the race's exact artifact — and the user keeps
            // working: edit-probes (didChange with a version bump and an appended trailing
            // comment, so line 7 never moves) until the reloaded database reaches the AST.
            //
            // Healed = Player resolves into the generated header. Asserted via definition,
            // not via "diagnostics became empty" (didClose also publishes an empty set, which
            // would fake that signal) — and polled until the answer is the CONVERGED one, not
            // merely non-null: during rebuild windows clangd answers definition via its
            // word-based navigation fallback, yielding the word's own occurrence in main.cpp,
            // a transiently WRONG answer a first-non-null poll would latch onto.
            File.WriteAllText(dbPath, realDb);
            await service.CloseDocumentAsync(mainCpp);
            await service.OpenDocumentAsync(mainCpp, MainCpp);

            var objGenPrefix = Path.GetFullPath(Path.Combine(dir, "obj", "gen")) + Path.DirectorySeparatorChar;
            LocationInfo? location = null;
            var version = 2;
            var deadline = DateTime.UtcNow + HealBudget;
            while (DateTime.UtcNow < deadline)
            {
                var candidate = await service.GetDefinitionAsync(mainCpp, line: 7, column: 6);
                if (candidate != null && IsGeneratedHeaderUnder(objGenPrefix, candidate))
                {
                    location = candidate;
                    break;
                }

                // The next keystroke: each update is what actually fetches a compile command,
                // and the content must genuinely change or the identical-inputs update may
                // never rebuild the AST.
                await service.ChangeDocumentAsync(mainCpp, MainCpp + $"// probe {version}\n", version);
                version++;
                await Task.Delay(EditProbeCadence);
            }

            Assert.That(location, Is.Not.Null,
                $"{HealBudget.TotalSeconds:F0}s of edits after the compile database appeared " +
                "never got clangd onto the real flags — the launch race would NOT self-heal " +
                "in a live session, which turns the fire-and-forget emission/start ordering " +
                "into a real product bug" +
                Environment.NewLine + "--- LSP traffic (tail) ---" + Environment.NewLine +
                Tail(raceOutput.Dump()));

            var fullPath = Path.GetFullPath(location!.Uri);
            Assert.That(fullPath, Does.StartWith(objGenPrefix).IgnoreCase,
                "the healed definition must resolve through the late-arriving database");
            Assert.That(fullPath, Does.EndWith(".g.h").IgnoreCase);
        }
        finally
        {
            if (service != null)
            {
                try { await service.StopAsync(); } catch { }
                try { service.Dispose(); } catch { }
                KillIfAlive(service.ServerProcessId);
            }

            try { Directory.Delete(dir, recursive: true); }
            catch
            {
                Thread.Sleep(250);
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_projectDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string ObjGen(string fileName) => Path.Combine(_projectDir, "obj", "gen", fileName);

    /// <summary>Whether <paramref name="location"/> points at a generated header under <paramref name="objGenPrefix"/>.</summary>
    private static bool IsGeneratedHeaderUnder(string objGenPrefix, LocationInfo location)
    {
        var fullPath = Path.GetFullPath(location.Uri);
        return fullPath.StartsWith(objGenPrefix, StringComparison.OrdinalIgnoreCase)
            && fullPath.EndsWith(".g.h", StringComparison.OrdinalIgnoreCase);
    }

    private static string DiagCodes(CppProjectBuildResult r) =>
        string.Join(", ", r.Diagnostics.Select(d => $"{d.Code}:{d.Message}"));

    private static string Labels(IReadOnlyList<CompletionItem> completions) =>
        string.Join(", ", completions.Take(25).Select(c => $"'{c.Label}'"));

    private static string Describe(IEnumerable<DiagnosticItem> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.FileName}({d.Line},{d.Column}) {d.Severity}: {d.Message}"));

    /// <summary>
    /// Mirrors <c>MainWindowViewModel.OnDiagnosticsReceived</c> faithfully: null-guard, decode
    /// the uri via <see cref="LanguageService.UriToPath"/>, then per-file replacement into the
    /// aggregator. The point is proving clangd's diagnostics reach the aggregator through the
    /// production event SHAPE — so the shape is reproduced, not reinvented.
    /// </summary>
    private void OnClangdDiagnostics(object? sender, DiagnosticsEventArgs e)
    {
        if (e?.Diagnostics == null) return;

        var filePath = LanguageService.UriToPath(e.Uri ?? "");
        if (string.IsNullOrEmpty(filePath)) return;

        _aggregator.SetFileDiagnostics(filePath, e.Diagnostics);
        lock (_publishLock)
        {
            _clangdPublishes.Add((filePath, e.Diagnostics));
        }
    }

    private (string FilePath, IReadOnlyList<DiagnosticItem> Diagnostics)? LatestPublishFor(string filePath)
    {
        lock (_publishLock)
        {
            return LatestUnlocked(_clangdPublishes, filePath);
        }
    }

    private static (string FilePath, IReadOnlyList<DiagnosticItem> Diagnostics)? LatestFor(
        List<(string FilePath, IReadOnlyList<DiagnosticItem> Diagnostics)> publishes,
        object gate, string filePath)
    {
        lock (gate)
        {
            return LatestUnlocked(publishes, filePath);
        }
    }

    private static (string FilePath, IReadOnlyList<DiagnosticItem> Diagnostics)? LatestUnlocked(
        List<(string FilePath, IReadOnlyList<DiagnosticItem> Diagnostics)> publishes, string filePath)
    {
        for (var i = publishes.Count - 1; i >= 0; i--)
        {
            if (string.Equals(publishes[i].FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                return publishes[i];
        }
        return null;
    }

    /// <summary>
    /// Bounded polling with a clear timeout message — the servers answer asynchronously
    /// (clangd parses the TU after didOpen; BasicLang analyzes after didOpen), so every
    /// judgment call polls up to a generous bound rather than sleeping a fixed amount.
    /// </summary>
    /// <param name="traffic">
    /// Where the LSP traffic log lives (a <see cref="RecordingOutput.Dump"/>), appended
    /// tail-truncated to the timeout verdict. This is what makes RecordingOutput's promise
    /// — "failures show the real traffic, not a bare assertion message" — hold on the
    /// TIMEOUT path too, not only on explicit Assert messages; same idiom as
    /// IdeInAngerTests' polled verdicts.
    /// </param>
    private static async Task<T> PollUntilAsync<T>(
        Func<Task<T>> probe, Func<T, bool> accepted, TimeSpan timeout, string what,
        Func<string>? traffic = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = default(T);
        while (DateTime.UtcNow < deadline)
        {
            last = await probe();
            if (accepted(last)) return last;
            await Task.Delay(300);
        }

        var message = $"Timed out after {timeout.TotalSeconds:F0}s waiting for: {what}";
        if (traffic != null)
        {
            message += Environment.NewLine + "--- LSP traffic (tail) ---" + Environment.NewLine
                       + Tail(traffic());
        }
        Assert.Fail(message);
        return last!; // unreachable — Assert.Fail throws
    }

    /// <summary>
    /// The last <paramref name="maxChars"/> of <paramref name="text"/> — recent traffic is
    /// what explains a timeout, and an unbounded dump would bury the verdict line.
    /// </summary>
    private static string Tail(string text, int maxChars = 8000) =>
        text.Length <= maxChars
            ? text
            : $"… ({text.Length - maxChars:N0} earlier chars truncated)" + Environment.NewLine
              + text.Substring(text.Length - maxChars);

    private static void KillIfAlive(int? pid)
    {
        if (pid is not int id) return;
        try
        {
            using var process = Process.GetProcessById(id);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // already gone — which is the desired state
        }
    }

    /// <summary>
    /// Thread-safe recording IOutputService so failures can show the real LSP traffic log
    /// instead of a bare assertion message.
    /// </summary>
    private sealed class RecordingOutput : IOutputService
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public string Dump() => string.Join(Environment.NewLine, _lines);

        public void WriteLine(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void Write(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void WriteError(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue("ERROR: " + message);
        public void Clear(OutputCategory category) { }
        public void ClearAll() { }
        public void Activate(OutputCategory category) { }
        public IReadOnlyList<string> GetMessages(OutputCategory category) => _lines.ToArray();
        public event EventHandler<OutputEventArgs>? OutputReceived { add { } remove { } }
        public IOutputChannel CreateChannel(string name) => throw new NotSupportedException();
        public IOutputChannel? GetChannel(string name) => null;
        public IReadOnlyList<IOutputChannel> Channels => Array.Empty<IOutputChannel>();
        public IOutputChannel? ActiveChannel { get; set; }
        public event EventHandler<string>? ChannelCreated { add { } remove { } }
        public event EventHandler<IOutputChannel?>? ActiveChannelChanged { add { } remove { } }
        public void ShowOutput() { }
    }
}
