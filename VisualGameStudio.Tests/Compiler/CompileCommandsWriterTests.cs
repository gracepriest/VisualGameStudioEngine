using System.Text.Json;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class CompileCommandsWriterTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-cc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Test]
    public void Write_EmitsOneEntryPerTu_WithDirectoryFileArguments()
    {
        var request = new CppCompileRequest
        {
            OutputPath = Path.Combine(_dir, "bin", "App.exe"),
            CppStandard = "c++20",
            WorkingDirectory = _dir,
        };
        request.SourceFiles.Add(Path.Combine(_dir, "main.cpp"));
        request.SourceFiles.Add(Path.Combine(_dir, "util.cpp"));
        request.IncludeDirs.Add(Path.Combine(_dir, "inc"));
        request.Defines.Add("FLAG");

        var path = CompileCommandsWriter.Write(
            _dir, CppToolchainKind.ClangLike, "clang++", request);

        Assert.That(path, Is.EqualTo(Path.Combine(_dir, "obj", "compile_commands.json")));
        Assert.That(File.Exists(path), Is.True);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var entries = doc.RootElement.EnumerateArray().ToList();
        Assert.That(entries, Has.Count.EqualTo(2));
        foreach (var e in entries)
        {
            Assert.That(e.GetProperty("directory").GetString(), Is.EqualTo(_dir));
            var args = e.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()).ToList();
            Assert.That(args[0], Is.EqualTo("clang++"));
            Assert.That(args, Does.Contain("-std=c++20"));
            Assert.That(args, Does.Contain("-I" + Path.Combine(_dir, "inc")));
            Assert.That(args, Does.Contain("-DFLAG"));
        }
        Assert.That(entries.Select(e => e.GetProperty("file").GetString()),
            Is.EquivalentTo(request.SourceFiles));
    }
}
