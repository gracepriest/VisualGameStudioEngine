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
    public void DefineConstants_AreSplitTrimmedNamedAndDeduplicated()
    {
        var warnings = new List<string>();
        Assert.That(BuildSymbols.For("javascript", "Debug", new[] { " TRACE ; DEBUG;;LEVEL=2,Extra" }, warnings),
            Is.EqualTo(new[] { "WEB", "DEBUG", "TRACE", "LEVEL", "Extra" }));
        Assert.That(warnings, Has.Count.EqualTo(1).And.Some.Contains("LEVEL=2"));
    }

    // ---- NAME=value (VB semantics): False/0 is NOT defined; True/-1/1/no value is; anything else is defined + warned.

    [TestCase("FLAG=False")]
    [TestCase("FLAG=false")]
    [TestCase("FLAG=FALSE")]
    [TestCase(" FLAG = 0 ")]
    public void AFalseValue_DoesNotDefineTheSymbol(string entry)
    {
        var warnings = new List<string>();
        Assert.That(BuildSymbols.For("csharp", null, new[] { entry }, warnings), Is.EqualTo(new[] { "DESKTOP" }));
        Assert.That(warnings, Is.Empty);
    }

    [TestCase("FLAG")]
    [TestCase("FLAG=True")]
    [TestCase(" FLAG = true ")]
    [TestCase("FLAG=-1")]
    [TestCase("FLAG=1")]
    [TestCase("FLAG=")]
    public void ATrueValueOrNoValue_DefinesTheSymbol(string entry)
    {
        var warnings = new List<string>();
        Assert.That(BuildSymbols.For("csharp", null, new[] { entry }, warnings), Is.EqualTo(new[] { "DESKTOP", "FLAG" }));
        Assert.That(warnings, Is.Empty);
    }

    [TestCase("FLAG=2")]
    [TestCase("FLAG=yes")]
    [TestCase("FLAG=\"x\"")]
    public void AnyOtherValue_DefinesTheSymbol_AndWarnsNamingTheEntry(string entry)
    {
        var warnings = new List<string>();
        Assert.That(BuildSymbols.For("csharp", null, new[] { entry }, warnings), Is.EqualTo(new[] { "DESKTOP", "FLAG" }));
        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain(entry.Trim()));
    }

    /// <summary>A False entry un-defines a symbol an earlier source defined (the configuration, or an earlier
    /// entry) — the last word wins, as a later /define does in VB.</summary>
    [Test]
    public void AFalseValue_UndefinesAnEarlierSymbol()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BuildSymbols.For("csharp", "Debug", new[] { "DEBUG=False" }), Is.EqualTo(new[] { "DESKTOP" }));
            Assert.That(BuildSymbols.For("csharp", null, new[] { "A;B;A=0" }), Is.EqualTo(new[] { "DESKTOP", "B" }));
            Assert.That(BuildSymbols.For("csharp", null, new[] { "A=False;A" }), Is.EqualTo(new[] { "DESKTOP", "A" }));
        });
    }

    /// <summary>The warning reaches the build's warning channel: a Warning-severity entry in AllErrors, which the
    /// CLI prints and the IDE lists — and it never fails the build.</summary>
    [Test]
    public void BasicCompiler_ReportsAnUnrecognisedDefineValue_AsAWarning()
    {
        var dir = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "bl-sym-" + System.Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = System.IO.Path.Combine(dir, "Main.bas");
            System.IO.File.WriteAllText(file,
                "Sub Main()\n#If LEVEL AndAlso Not OFF Then\nConsole.WriteLine(\"on\")\n" +
                "#Else\nDim = = )\n#End If\nEnd Sub\n");
            var options = new CompilerOptions { TargetBackend = "csharp" };
            options.DefineConstants.Add("LEVEL=2;OFF=False");
            var result = new BasicCompiler(options).CompileProjectFiles(new[] { file });
            var messages = string.Join(" | ", System.Linq.Enumerable.Select(result.AllErrors, e => e.Severity + ": " + e.Message));
            Assert.That(result.HasErrors, Is.False, messages);
            Assert.That(System.Linq.Enumerable.Count(result.AllErrors,
                    e => e.Severity == BasicLang.Compiler.SemanticAnalysis.ErrorSeverity.Warning && e.Message.Contains("LEVEL=2")),
                Is.EqualTo(1), messages);
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

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
