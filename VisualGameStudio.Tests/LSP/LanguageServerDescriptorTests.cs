using System.Diagnostics;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Utilities;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// Pins server identity: everything <c>LanguageService</c> used to hardcode about BasicLang
/// (the launch command, the settings key, the <c>languageId</c> on <c>didOpen</c>) now comes
/// from a <see cref="LanguageServerDescriptor"/>, so one client class can drive N servers.
///
/// <para>
/// Tests the pure statics — no server process is started. The existence probe behind
/// <c>BuildStartInfo</c>'s working directory is injected, mirroring
/// <c>ResolveLspPathOverride</c> / <c>ResolveWorkingDirectory</c>: this assembly has no
/// <c>InternalsVisibleTo</c> for VisualGameStudio.ProjectSystem, so a public static with an
/// injectable dependency is the only seam a test can reach.
/// </para>
/// </summary>
[TestFixture]
public class LanguageServerDescriptorTests
{
    private static ProcessStartInfo StartInfo(LanguageServerDescriptor descriptor, string? workspaceRoot) =>
        // `_ => true` = "the root exists". Task 4's existence guard is what makes an unusable root
        // cost us the cwd instead of the whole server (Process.Start THROWS on a missing
        // WorkingDirectory); it has its own tests. Injecting here keeps these hermetic — the roots
        // below are fictional paths, and probing the real filesystem for them would make every
        // WorkingDirectory assertion pass vacuously against "".
        LanguageService.BuildStartInfo(descriptor, workspaceRoot, _ => true);

    // ---- Launch ------------------------------------------------------------

    // BASICLANG MUST NOT MOVE. This is the command the IDE has always spawned, asserted whole:
    // `dotnet "<compiler>" --lsp`, quoted (the IDE installs under Program Files) and rooted at
    // the workspace.
    [Test]
    public void Descriptor_BasicLang_ProducesUnchangedStartInfo()
    {
        var d = LanguageServerDescriptor.BasicLang(compilerPath: @"C:\x\BasicLang.dll");

        var psi = StartInfo(d, @"C:\proj");

        Assert.Multiple(() =>
        {
            Assert.That(psi.FileName, Is.EqualTo("dotnet"),
                "the compiler is a managed assembly — the process is the .NET host, not the dll");
            Assert.That(psi.Arguments, Is.EqualTo(@"""C:\x\BasicLang.dll"" --lsp"));
            Assert.That(psi.WorkingDirectory, Is.EqualTo(@"C:\proj"));
            Assert.That(psi.RedirectStandardError, Is.True, "stderr MUST be drained or the server wedges");
        });
    }

    // BasicLang's server takes no root on its command line — it learns it from `initialize`.
    // Pins that adding clangd's root-derived flag did not leak into every descriptor.
    [TestCase(null)]
    [TestCase(@"C:\proj")]
    public void Descriptor_BasicLang_ArgumentsDoNotDependOnTheWorkspaceRoot(string? root)
    {
        var d = LanguageServerDescriptor.BasicLang(@"C:\x\BasicLang.dll");

        Assert.That(StartInfo(d, root).Arguments, Is.EqualTo(@"""C:\x\BasicLang.dll"" --lsp"));
    }

    // D1 (decided): the descriptor holds NO project-scoped state — it is built through DI at
    // startup, when no project is open. --compile-commands-dir is DERIVED from workspaceRoot at
    // BuildStartInfo time.
    [Test]
    public void Descriptor_Clangd_DerivesCompileCommandsDirFromWorkspaceRoot()
    {
        var d = LanguageServerDescriptor.Clangd(clangdPath: @"C:\llvm\clangd.exe");   // no project state

        var psi = StartInfo(d, @"C:\proj");

        Assert.That(psi.FileName, Is.EqualTo(@"C:\llvm\clangd.exe"),
            "clangd is spawned by absolute path, never by bare name");
        Assert.That(psi.Arguments, Does.Contain(@"--compile-commands-dir=C:\proj\obj"),
            "must match CompileCommandsWriter's output dir: <projectDir>/obj");
        Assert.That(psi.Arguments, Does.Not.Contain("--offset-encoding"),
            "Never pass --offset-encoding: the client's column math is utf-16 only. See plan landmine #3.");
    }

    // The value is a PATH, so it contains spaces on any real machine. Asserted as the WHOLE
    // argument string rather than by substring: `--compile-commands-dir="C:\My Projects\..."`
    // also *contains* the flag, but Windows' CommandLineToArgvW (how clangd's argv is built)
    // splits an unquoted token at the space, and clangd would silently ignore the fragment.
    // Quoting the whole token is the only form that survives.
    [Test]
    public void Descriptor_Clangd_CompileCommandsDir_IsOneArgvToken_EvenWithSpacesInThePath()
    {
        var d = LanguageServerDescriptor.Clangd(@"C:\llvm\clangd.exe");

        var psi = StartInfo(d, @"C:\My Projects\Game");

        Assert.That(psi.Arguments, Is.EqualTo(@"""--compile-commands-dir=C:\My Projects\Game\obj"""));
    }

    // THE ROOTLESS CASE. The IDE's autostart path starts the server with NO root (a real,
    // recorded gap the registry owns). clangd must then omit the flag rather than point at a
    // made-up directory: it falls back to searching upward from each file, which is degraded —
    // but a WRONG --compile-commands-dir is strictly worse than an absent one.
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Descriptor_Clangd_NoWorkspaceRoot_OmitsCompileCommandsDir_RatherThanInventingOne(string? root)
    {
        var d = LanguageServerDescriptor.Clangd(@"C:\llvm\clangd.exe");

        var psi = StartInfo(d, root);

        Assert.Multiple(() =>
        {
            Assert.That(psi.Arguments, Does.Not.Contain("--compile-commands-dir"));
            Assert.That(psi.Arguments, Is.Empty);
        });
    }

    // THE ASYMMETRY GUARD (Task 4's near-miss, one level up). The working directory, the root on
    // the `initialize` wire and clangd's --compile-commands-dir must all derive from ONE trim
    // rule. A second copy inside the descriptor would agree with it right up until it didn't.
    [Test]
    public void PaddedRoot_IsNormalizedOnce_ForBothTheWorkingDirectoryAndCompileCommandsDir()
    {
        var d = LanguageServerDescriptor.Clangd(@"C:\llvm\clangd.exe");

        var psi = StartInfo(d, "  C:\\proj  ");

        Assert.Multiple(() =>
        {
            Assert.That(psi.WorkingDirectory, Is.EqualTo(@"C:\proj"));
            Assert.That(psi.Arguments, Is.EqualTo(@"""--compile-commands-dir=C:\proj\obj"""));
        });
    }

    // THE PAIR. `obj` is hardcoded in TWO assemblies that cannot share a const — clangd's
    // --compile-commands-dir here in VisualGameStudio.Core, and compile_commands.json's output dir
    // in BasicLang's CompileCommandsWriter. Core does not reference BasicLang, and the reverse edge
    // (compiler → IDE abstractions) would be backwards, so the duplication is justified. A comment
    // saying "if that ever moves, both move together" is a WISH; this test is the mechanism.
    //
    // Both sides are computed by the real code — neither is a copy of the other's rule. If either
    // moves, clangd reads a directory the build never writes: zero IntelliSense on every C++ file,
    // no error on either side.
    [Test]
    public void CompileCommandsDir_PointsAtExactlyWhereTheBuildWritesTheCompilationDatabase()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "bl-ccdir-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDir);

        try
        {
            // What the BUILD writes. An empty request needs no toolchain: with no source files
            // CompileCommandsWriter emits an empty database, and the DIRECTORY is what we are here
            // for. The path comes back from the writer itself.
            var written = CompileCommandsWriter.Write(
                projectDir, CppToolchainKind.ClangLike, "clang++", new CppCompileRequest());

            // What the EDITOR reads.
            var arguments = StartInfo(LanguageServerDescriptor.Clangd(@"C:\llvm\clangd.exe"), projectDir).Arguments;

            Assert.Multiple(() =>
            {
                Assert.That(Path.GetFileName(written), Is.EqualTo("compile_commands.json"),
                    "clangd looks for this exact filename inside --compile-commands-dir");
                Assert.That(arguments, Is.EqualTo($"\"--compile-commands-dir={Path.GetDirectoryName(written)}\""),
                    "clangd must be pointed at exactly the directory CompileCommandsWriter writes to. " +
                    "These are two hardcoded 'obj' literals in two assemblies that cannot share a const — " +
                    "if you moved one, move the other.");
                Assert.That(File.Exists(written), Is.True, "the writer must actually create the database");
            });
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ---- Hardening ---------------------------------------------------------

    /// <summary>
    /// THE ROSTER — every descriptor that exists. Task 12's third server is added here ONCE and
    /// every invariant driven from it covers the newcomer automatically. Several hand-maintained
    /// copies of this list is how one of them silently stops covering what its name claims.
    /// </summary>
    private static IReadOnlyList<LanguageServerDescriptor> AllDescriptors() => new[]
    {
        LanguageServerDescriptor.BasicLang(@"C:\x\BasicLang.dll"),
        LanguageServerDescriptor.Clangd(@"C:\llvm\clangd.exe")
    };

    private static IEnumerable<TestCaseData> AllDescriptorCases() =>
        AllDescriptors().Select(d => new TestCaseData(d).SetName("{m}(" + d.Id + ")"));

    // THE 2026 CRITICAL FIXES. Every server, however launched, inherits all of them — that is
    // why BuildStartInfo takes the descriptor rather than each descriptor building its own
    // ProcessStartInfo. Each of these was a real, silent wedge:
    //  - a BOM on stdin corrupts the first Content-Length header and the server never replies;
    //  - an undrained stderr fills the ~4KB pipe buffer and the server blocks forever — clangd
    //    is chatty on stderr, so this is not optional for it.
    [TestCaseSource(nameof(AllDescriptorCases))]
    public void BuildStartInfo_EveryDescriptor_InheritsTheStdioHardening(LanguageServerDescriptor descriptor)
    {
        var psi = StartInfo(descriptor, @"C:\proj");

        Assert.Multiple(() =>
        {
            Assert.That(psi.RedirectStandardError, Is.True,
                "stderr MUST be drained or the server wedges once the pipe buffer fills");
            Assert.That(psi.RedirectStandardInput, Is.True);
            Assert.That(psi.RedirectStandardOutput, Is.True);
            Assert.That(psi.UseShellExecute, Is.False);
            Assert.That(psi.CreateNoWindow, Is.True);

            // The preamble, not the encoding's identity: accessing Process.StandardInput sets
            // AutoFlush, which writes the encoding's preamble before the first header. Encoding.UTF8
            // would inject EF BB BF and the handshake would never complete.
            Assert.That(psi.StandardInputEncoding, Is.Not.Null);
            Assert.That(psi.StandardInputEncoding!.GetPreamble(), Is.Empty,
                "StandardInputEncoding MUST be BOM-less — a preamble corrupts the first Content-Length header");
            Assert.That(psi.StandardOutputEncoding, Is.Not.Null);
            Assert.That(psi.StandardOutputEncoding!.GetPreamble(), Is.Empty);
        });
    }

    // ---- Identity ----------------------------------------------------------

    // Pins the id each server announces a document with. It does NOT pin "not a constant", despite
    // what the plan's name for it claimed: both servers have exactly one language id today, so an
    // implementation returning the per-server constant LanguageIds[0] passes both assertions. The
    // "reads the FILE" claim is carried by LanguageIdFor_FileTheDescriptorDoesNotOwn_Throws_*
    // (a constant cannot refuse a file) — a test name is a claim about the regressions it catches,
    // and this one's has been corrected to what it actually catches.
    [Test]
    public void Descriptor_LanguageIdFor_AnnouncesTheRoutingMapsIdForTheFile()
    {
        Assert.That(LanguageServerDescriptor.BasicLang("x").LanguageIdFor("a.bas"), Is.EqualTo("basiclang"));
        Assert.That(LanguageServerDescriptor.Clangd("x").LanguageIdFor("a.cpp"), Is.EqualTo("cpp"));   // 1-arg per D1
    }

    // clangd's SERVER id is "clangd" while the languageId it announces documents with is "cpp".
    // Sending "clangd" to a server would be meaningless; pin that they are not conflated.
    [Test]
    public void Descriptor_ServerId_IsNotTheLanguageId()
    {
        var clangd = LanguageServerDescriptor.Clangd("x");

        Assert.Multiple(() =>
        {
            Assert.That(clangd.Id, Is.EqualTo("clangd"));
            Assert.That(clangd.LanguageIdFor("a.cpp"), Is.EqualTo("cpp"));
            Assert.That(clangd.LanguageIdFor("a.h"), Is.EqualTo("cpp"), ".h is C++ by decision — clangd handles it");
        });
    }

    // A descriptor asked for a file it does not own must FAIL, not guess. Both directions are
    // wrong in their own way and neither may be silent:
    //  - .txt  → no server owns it; the routing map answers null, and a null languageId would be
    //            OMITTED by the serializer (DefaultIgnoreCondition.WhenWritingNull), producing a
    //            malformed didOpen with no error anywhere;
    //  - .cpp  → clangd owns it; answering "basiclang" (or the map's raw "cpp") would announce a
    //            document to a server that cannot parse it, which the server cannot detect.
    // CS8604 is in NoWarn repo-wide, so a `string?` return would compile clean into either.
    [TestCase("notes.txt", TestName = "unowned by every server")]
    [TestCase("main.cpp", TestName = "owned by another server")]
    [TestCase("engine.h", TestName = "owned by another server (header)")]
    [TestCase("project.blproj", TestName = "BasicLang-ish but not LSP-routed")]
    public void LanguageIdFor_FileTheDescriptorDoesNotOwn_Throws_RatherThanGuessing(string path)
    {
        var basicLang = LanguageServerDescriptor.BasicLang("x");

        var ex = Assert.Throws<ArgumentException>(() => basicLang.LanguageIdFor(path));
        Assert.That(ex!.Message, Does.Contain(path), "the message must name the file that was misrouted");
    }

    [TestCase("a.bas", true)]
    [TestCase("a.BAS", true)]
    [TestCase("a.bl", true)]
    [TestCase("a.mod", true)]
    [TestCase("a.cls", true)]
    [TestCase("a.class", true)]
    [TestCase("a.cpp", false)]
    [TestCase("a.h", false)]
    [TestCase("a.txt", false)]
    [TestCase("Makefile", false)]
    [TestCase(null, false)]
    public void Owns_BasicLang_MatchesTheRoutingMap(string? path, bool owned)
        => Assert.That(LanguageServerDescriptor.BasicLang("x").Owns(path), Is.EqualTo(owned));

    [TestCase("a.cpp", true)]
    [TestCase("a.cc", true)]
    [TestCase("a.cxx", true)]
    [TestCase("a.h", true)]
    [TestCase("a.hpp", true)]
    [TestCase("a.hh", true)]
    [TestCase("a.hxx", true)]
    [TestCase("a.inl", true)]
    [TestCase("a.bas", false)]
    [TestCase("a.c", false)]      // C is deliberately not routed in Phase 3a
    [TestCase("a.txt", false)]
    [TestCase(null, false)]
    public void Owns_Clangd_MatchesTheRoutingMap(string? path, bool owned)
        => Assert.That(LanguageServerDescriptor.Clangd("x").Owns(path), Is.EqualTo(owned));

    // THE DRIFT GUARD. Extensions is derived from the routing map, never hand-listed. If a
    // language is added to LanguageFileTypes and no descriptor claims it, its files reach no
    // server — silently. If two claimed it, routing would be ambiguous.
    [Test]
    public void EveryLspRoutedExtension_IsOwnedByExactlyOneDescriptor()
    {
        var descriptors = AllDescriptors();

        Assert.Multiple(() =>
        {
            foreach (var ext in LanguageFileTypes.LspRoutedExtensions)
            {
                var owners = descriptors.Where(d => d.Owns("file" + ext)).Select(d => d.Id).ToArray();
                Assert.That(owners, Has.Length.EqualTo(1),
                    $"{ext} must be owned by exactly one language server, was owned by " +
                    $"[{string.Join(", ", owners)}]");
                Assert.That(descriptors.Single(d => d.Id == owners[0]).Extensions, Does.Contain(ext),
                    $"{ext} routes to {owners[0]} but is missing from its Extensions — " +
                    "Extensions must be the inverse of the routing map, not a hand-listed copy");
            }
        });
    }

    /// <summary>
    /// The extension set each server is expected to own, written out literally: deriving the
    /// expectation from LspExtensionsFor (the code under test) would make it flip together with
    /// the code and pin nothing at all — the same reason CapabilityNegotiationTests hardcodes
    /// "utf-16". Keyed by server id so a descriptor added to the roster with no entry here fails
    /// loudly rather than going unasserted.
    /// </summary>
    private static readonly Dictionary<string, string[]> ExpectedExtensions = new()
    {
        ["basiclang"] = new[] { ".bas", ".bl", ".mod", ".cls", ".class" },
        // .c is deliberately absent — C is not routed in Phase 3a.
        ["clangd"] = new[] { ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hh", ".hxx", ".inl" }
    };

    [TestCaseSource(nameof(AllDescriptorCases))]
    public void Descriptor_Extensions_AreTheExtensionsTheServerOwns(LanguageServerDescriptor descriptor)
    {
        Assert.That(ExpectedExtensions.ContainsKey(descriptor.Id), Is.True,
            $"'{descriptor.Id}' is on the roster but has no expected extension set — add one here " +
            "rather than letting a new server's Extensions go unasserted");

        Assert.That(descriptor.Extensions, Is.EquivalentTo(ExpectedExtensions[descriptor.Id]));
    }

    [Test]
    public void LspExtensionsFor_LanguageNoServerOwns_IsEmpty_NotThrow()
        => Assert.That(LanguageFileTypes.LspExtensionsFor("no-such-language"), Is.Empty);

    // ServerPath is what must exist on disk; FileName is what gets started. They differ for
    // BasicLang — File.Exists("dotnet") is false (it is resolved via PATH), so probing FileName
    // would report the server missing on every machine.
    [Test]
    public void ServerPath_IsTheFileToProbeFor_NotAlwaysTheFileNameToStart()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LanguageServerDescriptor.BasicLang(@"C:\x\BasicLang.dll").ServerPath,
                Is.EqualTo(@"C:\x\BasicLang.dll"));
            Assert.That(LanguageServerDescriptor.BasicLang(@"C:\x\BasicLang.dll").FileName, Is.EqualTo("dotnet"));
            Assert.That(LanguageServerDescriptor.Clangd(@"C:\llvm\clangd.exe").ServerPath,
                Is.EqualTo(@"C:\llvm\clangd.exe"));
        });
    }

    // The key each server's path override lives under. BasicLang's is the one the settings
    // dialog already manages and SettingsConsumerContractTests pins a consumer for.
    [Test]
    public void SettingsKey_IsPerServer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LanguageServerDescriptor.BasicLang("x").SettingsKey, Is.EqualTo("basiclang.lsp.path"));
            Assert.That(LanguageServerDescriptor.Clangd("x").SettingsKey, Is.EqualTo("cpp.clangd.path"));
        });
    }
}
