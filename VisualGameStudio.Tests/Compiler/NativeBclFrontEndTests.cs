using System.Collections;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class NativeBclFrontEndTests
{
    private static ProgramNode Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    [Test]
    public void NumericLiteral_CarriesLexemeText()
    {
        var ast = Parse("Module M\n Sub Main()\n Dim d As Double = 1.50\n End Sub\nEnd Module");
        var lit = FindFirstNumericLiteral(ast);
        Assert.That(lit, Is.Not.Null);
        Assert.That(lit!.Text, Is.EqualTo("1.50"), "the literal's source text must survive parsing (scale is lost in the double 1.5)");
    }

    /// <summary>
    /// Small recursive walker over the AST that finds the first LiteralExpressionNode whose
    /// LiteralType is a numeric literal kind. Walks generically via reflection over every
    /// public property so it does not depend on a specific node's exact child shape.
    /// </summary>
    private static LiteralExpressionNode? FindFirstNumericLiteral(ASTNode node)
    {
        if (node is LiteralExpressionNode literal && IsNumericLiteral(literal.LiteralType))
        {
            return literal;
        }

        foreach (var property in node.GetType().GetProperties())
        {
            object? value;
            try
            {
                value = property.GetValue(node);
            }
            catch
            {
                continue;
            }

            if (value is ASTNode childNode)
            {
                var found = FindFirstNumericLiteral(childNode);
                if (found != null)
                {
                    return found;
                }
            }
            else if (value is IEnumerable enumerable and not string)
            {
                foreach (var item in enumerable)
                {
                    if (item is ASTNode childItem)
                    {
                        var found = FindFirstNumericLiteral(childItem);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static bool IsNumericLiteral(TokenType literalType)
    {
        return literalType is TokenType.IntegerLiteral or TokenType.LongLiteral
            or TokenType.SingleLiteral or TokenType.DoubleLiteral;
    }

    [Test]
    public void Byte_IsUnsigned_SByte_IsSigned_InAnalyzerHelpers()
    {
        Assert.That(new TypeInfo("SByte", TypeKind.Primitive).IsSigned(), Is.True);
        Assert.That(new TypeInfo("SByte", TypeKind.Primitive).IsNumeric(), Is.True);
        Assert.That(new TypeInfo("SByte", TypeKind.Primitive).IsIntegral(), Is.True);
        Assert.That(new TypeInfo("Byte", TypeKind.Primitive).IsUnsigned(), Is.True);
        Assert.That(new TypeInfo("Byte", TypeKind.Primitive).IsSigned(), Is.False);
    }

    [Test]
    [Category("Integration")]
    public void SByte_Arithmetic_PassesAnalysis_And_RunsOnCSharp()
    {
        var src = @"Module M
 Sub Main()
  Dim a As SByte = 5
  Dim b As SByte = -3
  Console.WriteLine(a + b)
 End Sub
End Module";
        Assert.That(CompileRunCSharp(src), Is.EqualTo("2"));
    }

    // ====================================================================
    // Decimal analyzer gates (P1 Task 3, spec 6.1) — no literal contexts yet
    // ====================================================================

    [TestCase("Dim c As Decimal = a + b")]
    [TestCase("Dim c As Decimal = a + 1")]
    [TestCase("Dim c As Decimal = 1 + a")]      // symmetric integral widening
    [TestCase("Dim c As Decimal = a * b")]
    [TestCase("Dim ok As Boolean = a < b")]
    [TestCase("Dim c As Decimal = -a")]         // unary minus
    [TestCase("a += 1")]                        // compound with integral
    public void Decimal_OperatorGates_Analyze(string stmt)
        => AssertAnalyzesClean(Wrap("Dim a As Decimal = 1\nDim b As Decimal = 2\n" + stmt));

    [TestCase("Dim c = a + x")]                 // arithmetic mix
    [TestCase("Dim c = a + 0.5")]               // Double literal in operand position stays an error until Task 4
    [TestCase("Dim ok As Boolean = a < x")]     // comparison mix
    [TestCase("a += 0.5")]                      // compound mix (Double literal) stays an error until Task 4
    public void Decimal_Op_Double_Errors_WithCTypeHint(string stmt)
        => AssertAnalysisError(Wrap("Dim a As Decimal = 1\nDim x As Double = 0.5\n" + stmt),
            new[] { "Decimal", "CType" });

    /// <summary>
    /// Decimal + Long must type Decimal, not Long (the Decimal rung sits BEFORE
    /// the Double/Single/Long ladder in GetCommonType). TypeManager is public and
    /// reachable from tests, so this is the direct unit assertion; the
    /// analyzer-level proof rides along in <see cref="Decimal_PlusLong_ResultIsNotDoubleAssignable"/>.
    /// Decimal is not registered in TypeManager's built-in table (it resolves via
    /// the analyzer's .NET-type fallback), so the operand is constructed directly.
    /// </summary>
    [Test]
    public void Decimal_CommonType_BeatsLadder()
    {
        var tm = new TypeManager();
        var dec = new TypeInfo("Decimal", TypeKind.Class);

        Assert.That(tm.GetCommonType(dec, tm.LongType)?.Name, Is.EqualTo("Decimal"));
        Assert.That(tm.GetCommonType(tm.LongType, dec)?.Name, Is.EqualTo("Decimal"));
        Assert.That(tm.GetCommonType(dec, tm.IntegerType)?.Name, Is.EqualTo("Decimal"));
        Assert.That(tm.GetCommonType(dec, dec)?.Name, Is.EqualTo("Decimal"));

        // Decimal op Single/Double has no implicit common type: null sentinel,
        // the binary-visit call site raises the CType-hinted error (spec 6.1).
        Assert.That(tm.GetCommonType(dec, tm.DoubleType), Is.Null);
        Assert.That(tm.GetCommonType(tm.DoubleType, dec), Is.Null);
        Assert.That(tm.GetCommonType(dec, tm.SingleType), Is.Null);
        Assert.That(tm.GetCommonType(tm.SingleType, dec), Is.Null);
    }

    /// <summary>
    /// Analyzer-level ladder proof: if 'a + l' typed Long (ladder win) or Double,
    /// the Double assignment would be legal — the error proves the result is Decimal.
    /// The Decimal assignment right above it must stay clean (Decimal <- Decimal).
    /// </summary>
    [Test]
    public void Decimal_PlusLong_ResultIsNotDoubleAssignable()
    {
        AssertAnalyzesClean(Wrap("Dim a As Decimal = 1\nDim l As Long = 2\nDim c As Decimal = a + l"));
        AssertAnalysisError(Wrap("Dim a As Decimal = 1\nDim l As Long = 2\nDim d As Double = a + l"),
            new[] { "Decimal", "Double" });
    }

    [Test]
    public void Decimal_AssignmentRules_IntegralWidens_FloatingRejected()
    {
        // Non-literal integral -> Decimal is legal (spec 6.1 assignment rules).
        AssertAnalyzesClean(Wrap("Dim l As Long = 2\nDim c As Decimal = l"));
        // Non-literal floating -> Decimal and Decimal -> floating are both illegal.
        AssertAnalysisError(Wrap("Dim x As Double = 0.5\nDim c As Decimal = x"),
            new[] { "Decimal", "CType" });
        AssertAnalysisError(Wrap("Dim a As Decimal = 1\nDim y As Double = a"),
            new[] { "Decimal", "Double" });
    }

    /// <summary>
    /// A floating-point LITERAL initializer stays an error until Task 4 adds
    /// Decimal-context literal conversion (the IsNumericLiteralAssignable
    /// carve-out must not smuggle 1.5 into a Decimal through double space).
    /// Task 4 flips this assertion to clean.
    /// </summary>
    [Test]
    public void Decimal_FloatingLiteralInit_ErrorsUntilTask4()
        => AssertAnalysisError(Wrap("Dim a As Decimal = 1.5"), new[] { "Decimal", "CType" });

    // ====================================================================
    // Analyzer-level helpers
    // ====================================================================

    private static string Wrap(string body)
        => "Module M\n Sub Main()\n" + body + "\n End Sub\nEnd Module";

    /// <summary>
    /// Runs Lexer -> Parser -> SemanticAnalyzer (the standard analyzer-test idiom,
    /// cf. TypeInferenceTests) and returns the semantic error messages. Parse
    /// errors fail the test immediately — every input here must be syntactically valid.
    /// </summary>
    private static List<string> AnalyzeErrors(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.Select(e => e.Message).ToList();
    }

    private static void AssertAnalyzesClean(string source)
    {
        var errors = AnalyzeErrors(source);
        Assert.That(errors, Is.Empty,
            "expected clean analysis but got:\n" + string.Join("\n", errors));
    }

    private static void AssertAnalysisError(string source, string[] expectContains)
    {
        var errors = AnalyzeErrors(source);
        Assert.That(errors, Is.Not.Empty, "expected a semantic error but analysis was clean");
        Assert.That(errors.Any(msg => expectContains.All(msg.Contains)),
            $"no error contained all of [{string.Join(", ", expectContains)}]; got:\n" + string.Join("\n", errors));
    }

    // ====================================================================
    // C# backend smoke: integer-valued Decimal programs avoid the Task-4
    // literal gap (10 and 3 emit as plain integer constants, valid C#).
    // ====================================================================

    [Test]
    [Category("Integration")]
    public void Decimal_IntegerArithmetic_RunsOnCSharp()
        => Assert.That(CompileRunCSharp(@"Module M
 Sub Main()
  Dim a As Decimal = 10
  Dim b As Decimal = 3
  Console.WriteLine(a + b)
  Console.WriteLine(a - b)
  Console.WriteLine(a < b)
 End Sub
End Module"), Is.EqualTo("13\n7\nFalse"));

    // ====================================================================
    // C# end-to-end helper (reused by later native-BCL tasks)
    // ====================================================================

    /// <summary>
    /// Compiles the given BasicLang source through the real CLI
    /// (`BasicLang.exe build` on the CSharp backend — the same binary the suite
    /// deploys next to the tests via the project reference, so it always carries
    /// the compiler changes under test), runs the produced executable, and
    /// returns its stdout with line endings normalized to \n and the trailing
    /// newline trimmed. Callers must be tagged [Category("Integration")].
    /// </summary>
    private static string CompileRunCSharp(string src)
    {
        if (!DotnetOnPath())
        {
            Assert.Ignore("dotnet SDK not found on PATH — the CLI's C# backend cannot build the generated project.");
        }

        var rootDir = Path.Combine(Path.GetTempPath(), "bl-nativebcl-e2e-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(rootDir, "App");
        Directory.CreateDirectory(projectDir);
        try
        {
            File.WriteAllText(Path.Combine(projectDir, "Main.bas"), src);
            File.WriteAllText(Path.Combine(projectDir, "App.blproj"),
@"<?xml version=""1.0"" encoding=""utf-8""?>
<BasicLangProject Version=""1.0"">
  <PropertyGroup>
    <ProjectName>App</ProjectName>
    <OutputType>Exe</OutputType>
    <TargetBackend>CSharp</TargetBackend>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Main.bas"" />
  </ItemGroup>
</BasicLangProject>
");

            // Build via the CLI. 120s: a first dotnet build in a fresh temp dir
            // includes restore, which can exceed 60s on a cold cache.
            var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(),
                new[] { "build", Path.Combine(projectDir, "App.blproj") },
                projectDir,
                timeoutMs: 120_000);
            Assert.That(buildExit, Is.EqualTo(0),
                $"CLI C# build failed.\nSTDOUT:\n{buildOut}\nSTDERR:\n{buildErr}");

            var exes = Directory.GetFiles(projectDir, "App.exe", SearchOption.AllDirectories);
            Assert.That(exes, Is.Not.Empty,
                $"CLI build claimed success but produced no App.exe.\nSTDOUT:\n{buildOut}");

            var (runExit, runOut, runErr) = CliTestHarness.RunProcess(
                exes[0], Array.Empty<string>(), Path.GetDirectoryName(exes[0])!, timeoutMs: 60_000);
            Assert.That(runExit, Is.EqualTo(0),
                $"compiled program exited nonzero ({runExit}).\nSTDOUT:\n{runOut}\nSTDERR:\n{runErr}");

            return runOut.Replace("\r\n", "\n").TrimEnd('\n');
        }
        finally
        {
            try { Directory.Delete(rootDir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    private static bool DotnetOnPath()
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        return paths.Any(p => !string.IsNullOrWhiteSpace(p) &&
            (File.Exists(Path.Combine(p.Trim(), "dotnet.exe")) || File.Exists(Path.Combine(p.Trim(), "dotnet"))));
    }
}
