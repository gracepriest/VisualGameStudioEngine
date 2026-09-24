using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The C# backend's NON-floating literal paths — the sibling of <see cref="CSharpFloatLiteralTests"/>,
/// which owns Single/Double. Three defects, all measured on master 42a2280c before the fix:
///
/// <para>1. <c>EmitConstant</c>'s integral fallback was a CurrentCulture <c>ToString()</c>. sv-SE's
/// NegativeSign is U+2212, so <c>Public NegI As Integer = -7</c> emitted <c>public int NegI = −7;</c>
/// — CS1056 (same for Long).</para>
///
/// <para>2. The enum member line interpolated <c>{member.Value}</c>: sv-SE <c>Back = −1</c> — CS1056.</para>
///
/// <para>3. <c>FormatDefaultValue</c> kept its own copy of the literal rules: no Char arm
/// (<c>Optional c As Char = "a"c</c> → <c>char c = a</c>, CS0103 in EVERY culture), String defaults
/// unescaped (<c>"a""b\\c"</c> → <c>string s = "a"bc"</c>… CS1003/CS1010), and the same
/// CurrentCulture fallback (sv-SE <c>int n = −5</c>, CS1056). It now delegates to
/// <c>EmitConstant</c>.</para>
///
/// <para>Note: BasicLang string literals take BACKSLASH escapes (<c>\\</c>, <c>\"</c>, <c>\n</c>) as
/// well as the VB doubled quote, so <c>"a""b\\c"</c> is the four characters <c>a"b\c</c>.</para>
/// </summary>
[TestFixture]
public class CSharpLiteralCultureTests
{
    private const string U2212 = "−";

    // ---- 1. the integral fallback -------------------------------------------------------------

    private const string NegativeIntegralFields = """
        Class Box
         Public NegI As Integer = -7
         Public NegL As Long = -9
        End Class

        Module M
         Sub Main()
         End Sub
        End Module
        """;

    [Test]
    [SetCulture("sv-SE")]
    public void UnderSvSECulture_NegativeIntegralFields_UseAsciiMinusAndCompile()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(NegativeIntegralFields);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Not.Contain(U2212), cs);
            Assert.That(cs, Does.Contain("NegI = -7"), cs);
            Assert.That(cs, Does.Contain("NegL = -9"), cs);
        });
        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(NegativeIntegralFields);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }

    // ---- 2. the enum member line --------------------------------------------------------------

    private const string NegativeEnumMember = """
        Enum Dir
         Forward = 0
         Back = -1
        End Enum

        Module M
         Sub Main()
         End Sub
        End Module
        """;

    [Test]
    [SetCulture("sv-SE")]
    public void UnderSvSECulture_NegativeEnumMember_UsesAsciiMinusAndCompiles()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(NegativeEnumMember);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Not.Contain(U2212), cs);
            Assert.That(cs, Does.Contain("Back = -1"), cs);
        });
        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(NegativeEnumMember);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }

    // ---- 3. FormatDefaultValue ----------------------------------------------------------------

    private const string NegativeIntegerDefault = """
        Module M
         Function Pick(Optional n As Integer = -5) As Integer
          Return n
         End Function
         Sub Main()
          Console.WriteLine(Pick())
         End Sub
        End Module
        """;

    [Test]
    [SetCulture("sv-SE")]
    public void UnderSvSECulture_NegativeIntegerDefault_UsesAsciiMinusAndCompiles()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(NegativeIntegerDefault);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Not.Contain(U2212), cs);
            Assert.That(cs, Does.Contain("int n = -5"), cs);
        });
        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(NegativeIntegerDefault);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }

    internal const string CharDefault = """
        Module M
         Function Echo(Optional c As Char = "a"c) As Char
          Return c
         End Function
         Sub Main()
          Console.WriteLine(Echo())
         End Sub
        End Module
        """;

    /// <summary>Culture-independent: <c>char c = a</c> was CS0103 everywhere.</summary>
    [Test]
    [NonParallelizable] // FourBackends' C# leg redirects Console.Out
    public void OptionalCharDefault_IsAQuotedCharLiteral_AndRuns()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(CharDefault);
        Assert.That(cs, Does.Contain("char c = 'a'"), cs);
        Assert.That(FourBackends.RunEmittedCSharpText(cs).Trim(), Is.EqualTo("a"));
    }

    /// <summary>The four characters <c>a"b\c</c>: a doubled quote AND a backslash escape.</summary>
    internal const string StringDefault = """
        Module M
         Function Echo(Optional s As String = "a""b\\c") As String
          Return s
         End Function
         Sub Main()
          Console.WriteLine(Echo())
         End Sub
        End Module
        """;

    [Test]
    [NonParallelizable] // FourBackends' C# leg redirects Console.Out
    public void OptionalStringDefault_WithQuoteAndBackslash_IsEscaped_AndRuns()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(StringDefault);
        Assert.That(cs, Does.Contain("string s = \"a\\\"b\\\\c\""), cs);
        Assert.That(FourBackends.RunEmittedCSharpText(cs).Trim(), Is.EqualTo("a\"b\\c"));
    }

    // ---- the IDE entry point (BasicCompiler.CompileProjectFiles) ------------------------------

    /// <summary>
    /// The IDE build path generates C# from <c>CombinedIR</c>, not through the hand-assembled
    /// pipeline <c>EmitCSharpForTest</c> uses. All three defects in one program, under sv-SE.
    /// </summary>
    [Test]
    [SetCulture("sv-SE")]
    [NonParallelizable] // FourBackends' C# leg redirects Console.Out
    public void CompileProjectFiles_UnderSvSECulture_EmitsInvariantLiteralsThatRun()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-cs-lit-culture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, """
                Enum Dir
                 Forward = 0
                 Back = -1
                End Enum

                Class Box
                 Public NegI As Integer = -7
                End Class

                Module M
                 Function Pick(Optional n As Integer = -5) As Integer
                  Return n
                 End Function
                 Function Ch(Optional c As Char = "a"c) As Char
                  Return c
                 End Function
                 Function Str(Optional s As String = "a""b\\c") As String
                  Return s
                 End Function
                 Sub Main()
                  Dim b As New Box()
                  ' +10 keeps the RUN's own output non-negative: this test runs in-process under
                  ' sv-SE, where formatting a negative at run time would itself print U+2212.
                  Console.WriteLine(CStr(b.NegI + 10) & "|" & CStr(Pick() + 10) & "|" & Ch() & "|" & Str())
                 End Sub
                End Module
                """);

            var result = new BasicCompiler(new CompilerOptions { TargetBackend = "csharp" }).CompileProjectFiles(new[] { path });
            Assert.That(result.Success, Is.True, string.Join("; ", result.AllErrors.Select(e => e.Message)));

            var cs = new ImprovedCSharpCodeGenerator().Generate(result.CombinedIR);
            Assert.Multiple(() =>
            {
                Assert.That(cs, Does.Not.Contain(U2212), cs);
                Assert.That(cs, Does.Contain("Back = -1"), cs);
                Assert.That(cs, Does.Contain("NegI = -7"), cs);
                Assert.That(cs, Does.Contain("int n = -5"), cs);
                Assert.That(cs, Does.Contain("char c = 'a'"), cs);
                Assert.That(cs, Does.Contain("string s = \"a\\\"b\\\\c\""), cs);
            });
            Assert.That(FourBackends.RunEmittedCSharpText(cs).Trim(), Is.EqualTo("3|5|a|a\"b\\c"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

/// <summary>The CLI entry point (<c>BasicLang.exe build</c>): the Char and String defaults, built and run.</summary>
[TestFixture]
[Category("Integration")]
public class CSharpLiteralCultureCliTests
{
    [Test]
    public void Cli_OptionalCharDefault_Runs() =>
        Assert.That(CliTestHarness.CompileRunCSharp(CSharpLiteralCultureTests.CharDefault).Trim(), Is.EqualTo("a"));

    [Test]
    public void Cli_OptionalStringDefault_WithQuoteAndBackslash_Runs() =>
        Assert.That(CliTestHarness.CompileRunCSharp(CSharpLiteralCultureTests.StringDefault).Trim(), Is.EqualTo("a\"b\\c"));
}
