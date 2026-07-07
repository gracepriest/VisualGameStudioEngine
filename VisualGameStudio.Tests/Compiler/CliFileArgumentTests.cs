using NUnit.Framework;
using BasicLang.Compiler.Driver;
using System;
using System.IO;
using System.Threading.Tasks;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Tests for the BasicLang CLI file-argument handling (<see cref="Program.Main"/>).
/// A standalone source file named on the command line must route through the
/// compile path, not be silently ignored in favour of the multi-target demo
/// pipeline. Regression: .cls/.class were missing from the recognised-extension
/// list, so `BasicLang.exe Thing.cls` fell through to demo mode.
/// </summary>
[TestFixture]
public class CliFileArgumentTests
{
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BasicLang_CliTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// `BasicLang.exe Thing.cls` must route through the compile path (which
    /// prints "Compiling:") instead of the demo pipeline (which prints the
    /// "Pipeline Demo" banner).
    /// </summary>
    [Test]
    [NonParallelizable]
    public async Task Cli_CompilesStandaloneClsFile()
    {
        var clsPath = Path.Combine(_tempDir, "Thing.cls");
        File.WriteAllText(clsPath, @"Public Value As Integer

Sub New()
    Me.Value = 1
End Sub
");

        var originalOut = Console.Out;
        var captured = new StringWriter();
        int exit;
        try
        {
            Console.SetOut(captured);
            exit = await Program.Main(new[] { clsPath });
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = captured.ToString();
        Assert.That(output, Does.Contain("Compiling:"),
            "standalone .cls did not route through the compile path");
        Assert.That(output, Does.Not.Contain("Pipeline Demo"),
            "standalone .cls fell through to the demo pipeline instead of compiling");
        Assert.That(exit, Is.EqualTo(0),
            $"expected a successful compile, CLI output was:\n{output}");
    }

    /// <summary>
    /// A positional argument the CLI cannot interpret (not a flag, subcommand,
    /// or compilable file) must produce an error + usage help and a non-zero
    /// exit, instead of silently falling through to the demo pipeline.
    /// </summary>
    [Test]
    [NonParallelizable]
    public async Task Cli_UnrecognizedArgument_PrintsUsageAndExitsNonZero()
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var captured = new StringWriter();
        int exit;
        try
        {
            Console.SetOut(captured);
            Console.SetError(captured);
            exit = await Program.Main(new[] { "Thing.bass" });
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        var output = captured.ToString();
        Assert.That(exit, Is.EqualTo(2), $"expected exit code 2, CLI output was:\n{output}");
        Assert.That(output, Does.Contain("unrecognized argument"),
            "expected an 'unrecognized argument' diagnostic");
        Assert.That(output, Does.Contain("Thing.bass"),
            "the diagnostic should name the offending argument");
        Assert.That(output, Does.Contain("Usage:"),
            "expected usage help to accompany the error");
        Assert.That(output, Does.Not.Contain("Pipeline Demo"),
            "an unrecognized argument must not fall through to the demo pipeline");
    }

    /// <summary>
    /// With no positional arguments at all, the CLI preserves its showcase
    /// behaviour and runs the multi-target demo pipeline (exit 0).
    /// </summary>
    [Test]
    [NonParallelizable]
    public async Task Cli_NoArguments_RunsDemoPipeline()
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var captured = new StringWriter();
        int exit;
        try
        {
            Console.SetOut(captured);
            Console.SetError(captured);
            exit = await Program.Main(Array.Empty<string>());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        var output = captured.ToString();
        Assert.That(exit, Is.EqualTo(0), $"expected exit code 0, CLI output was:\n{output}");
        Assert.That(output, Does.Contain("Pipeline Demo"),
            "no-argument invocation should still run the demo showcase");
        Assert.That(output, Does.Not.Contain("unrecognized argument"),
            "no-argument invocation must not be treated as an error");
    }
}
