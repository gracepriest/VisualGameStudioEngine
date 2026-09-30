using System.Collections.Generic;
using BasicLang.Compiler;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>Spec §4.1 / O16 / O19 — the ONE answer to "which conditional-compilation symbols does this build define".</summary>
[TestFixture]
public class BuildSymbolTests
{
    [TestCase("javascript", "WEB")]
    [TestCase("js", "WEB")]
    [TestCase("JavaScript", "WEB")]
    [TestCase("csharp", "DESKTOP")]
    [TestCase("cpp", "DESKTOP")]
    [TestCase("msil", "DESKTOP")]
    [TestCase("llvm", "DESKTOP")]
    [TestCase(null, "DESKTOP")]
    public void TheTargetSymbol(string? backend, string expected) =>
        Assert.That(BuildSymbols.For(backend, null, null), Is.EqualTo(new[] { expected }));

    [TestCase("Debug", "DEBUG")]
    [TestCase("debug", "DEBUG")]
    [TestCase("Release", "RELEASE")]
    public void TheConfigurationSymbol(string configuration, string expected) =>
        Assert.That(BuildSymbols.For("csharp", configuration, null), Is.EqualTo(new[] { "DESKTOP", expected }));

    [Test]
    public void ACustomConfiguration_DefinesNeither() =>
        Assert.That(BuildSymbols.For("csharp", "Staging", null), Is.EqualTo(new[] { "DESKTOP" }));

    [Test]
    public void DefineConstants_AreSplitTrimmedNamedAndDeduplicated() =>
        Assert.That(BuildSymbols.For("javascript", "Debug", new[] { " TRACE ; DEBUG;;LEVEL=2,Extra" }),
            Is.EqualTo(new[] { "WEB", "DEBUG", "TRACE", "LEVEL", "Extra" }));

    /// <summary>The compiler object defines them — every route that builds a BasicCompiler from options gets them.</summary>
    [Test]
    public void BasicCompiler_DefinesTheSymbolsItsOptionsName()
    {
        var dir = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "bl-sym-" + System.Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = System.IO.Path.Combine(dir, "Main.bas");
            System.IO.File.WriteAllText(file,
                "Sub Main()\n#If WEB AndAlso RELEASE Then\nConsole.WriteLine(\"web release\")\n" +
                // ⚠ The inactive arm must be a DEFINITE error: a bare identifier statement (the plan's
                // `ThisIsNotCode`) compiles silently, which left this test green with no symbols defined.
                "#Else\nDim = = )\n#End If\nEnd Sub\n");
            var result = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript", Configuration = "Release" })
                .CompileProjectFiles(new[] { file });
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", System.Linq.Enumerable.Select(result.AllErrors, e => e.Message)));
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }
}
