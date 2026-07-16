using System.Diagnostics;
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

    // ---- Hardening ---------------------------------------------------------

    private static IEnumerable<TestCaseData> AllDescriptors()
    {
        yield return new TestCaseData(LanguageServerDescriptor.BasicLang(@"C:\x\BasicLang.dll")).SetName("BasicLang");
        yield return new TestCaseData(LanguageServerDescriptor.Clangd(@"C:\llvm\clangd.exe")).SetName("Clangd");
    }

    // THE 2026 CRITICAL FIXES. Every server, however launched, inherits all of them — that is
    // why BuildStartInfo takes the descriptor rather than each descriptor building its own
    // ProcessStartInfo. Each of these was a real, silent wedge:
    //  - a BOM on stdin corrupts the first Content-Length header and the server never replies;
    //  - an undrained stderr fills the ~4KB pipe buffer and the server blocks forever — clangd
    //    is chatty on stderr, so this is not optional for it.
    [TestCaseSource(nameof(AllDescriptors))]
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

    [Test]
    public void Descriptor_LanguageId_ComesFromTheFile_NotAConstant()
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
        var descriptors = new[]
        {
            LanguageServerDescriptor.BasicLang("x"),
            LanguageServerDescriptor.Clangd("x")
        };

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

    // The extension sets are written out literally rather than compared against
    // LspExtensionsFor — deriving the expectation from the code under test would make this flip
    // together with the code and pin nothing at all (the same reason CapabilityNegotiationTests
    // hardcodes "utf-16").
    [Test]
    public void Descriptor_Extensions_AreTheExtensionsTheServerOwns()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LanguageServerDescriptor.BasicLang("x").Extensions,
                Is.EquivalentTo(new[] { ".bas", ".bl", ".mod", ".cls", ".class" }));
            Assert.That(LanguageServerDescriptor.Clangd("x").Extensions,
                Is.EquivalentTo(new[] { ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hh", ".hxx", ".inl" }),
                ".c is deliberately absent — C is not routed in Phase 3a");
            Assert.That(LanguageFileTypes.LspExtensionsFor("no-such-language"), Is.Empty,
                "a language no server owns must yield no extensions, not throw");
        });
    }

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
