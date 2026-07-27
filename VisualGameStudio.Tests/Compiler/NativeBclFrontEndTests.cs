using System.Collections;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
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
    // Decimal analyzer gates (P1 Task 3, spec 6.1) + literal contexts (Task 4)
    // ====================================================================

    [TestCase("Dim c As Decimal = a + b")]
    [TestCase("Dim c As Decimal = a + 1")]
    [TestCase("Dim c As Decimal = 1 + a")]      // symmetric integral widening
    [TestCase("Dim c As Decimal = a * b")]
    [TestCase("Dim ok As Boolean = a < b")]
    [TestCase("Dim c As Decimal = -a")]         // unary minus
    [TestCase("a += 1")]                        // compound with integral
    [TestCase("Dim c = a + 0.5")]               // operand position IS a Decimal context (spec 6.1, Task 4)
    [TestCase("a += 0.5")]                      // compound with a floating literal — Decimal context too
    public void Decimal_OperatorGates_Analyze(string stmt)
        => AssertAnalyzesClean(Wrap("Dim a As Decimal = 1\nDim b As Decimal = 2\n" + stmt));

    [TestCase("Dim c = a + x")]                 // arithmetic mix (non-literal Double)
    [TestCase("Dim ok As Boolean = a < x")]     // comparison mix (non-literal Double)
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
    /// A floating-point LITERAL initializer is legal (P1 Task 4): the Dim
    /// initializer is a Decimal context, so the literal converts at compile
    /// time from its SOURCE TEXT (never through double space) and retypes to
    /// Decimal. Flipped from the Task-3 pin that asserted this errored.
    /// The value/scale proof runs end-to-end in
    /// <see cref="DecimalLiteral_InitAndScale_OnCSharp"/>.
    /// </summary>
    [Test]
    public void Decimal_FloatingLiteralInit_Works()
        => AssertAnalyzesClean(Wrap("Dim a As Decimal = 1.5"));

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
    // Decimal literals in Decimal contexts (P1 Task 4, spec 6.1):
    // compile-time conversion from the literal's SOURCE TEXT, System.Decimal
    // IR constants, m-suffix C# emission, CType in both directions.
    // Every expected string below was verified against real .NET first.
    // ====================================================================

    /// <summary>
    /// Scale must survive: "1.50" prints as 1.50, which is only possible when
    /// the constant was parsed from the source text (the double 1.5 has no
    /// scale). Verified real .NET: decimal.Parse("1.50", InvariantCulture)
    /// ToString → "1.50".
    /// </summary>
    [Test]
    [Category("Integration")]
    public void DecimalLiteral_InitAndScale_OnCSharp()
        => Assert.That(CompileRunCSharp(@"Module M
 Sub Main()
  Dim x As Decimal = 1.50
  Console.WriteLine(x)
 End Sub
End Module"), Is.EqualTo("1.50"));

    /// <summary>
    /// Operand position is a Decimal context: 'd * 1.08' converts 1.08 from
    /// text to 1.08m (the money pattern). Verified real .NET:
    /// 19.99m * 1.08m = 21.5892.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void DecimalLiteral_OperandPosition_MoneyPattern()
        => Assert.That(CompileRunCSharp(@"Module M
 Sub Main()
  Dim d As Decimal = 19.99
  Console.WriteLine(d * 1.08)
 End Sub
End Module"), Is.EqualTo("21.5892"));

    /// <summary>
    /// The remaining three Decimal contexts in one program: a literal argument
    /// to a Decimal parameter (2.25), Return 1.5 from a Decimal Function, and
    /// For d As Decimal = 0 To 1 Step 0.25 (5 iterations, total 2.50).
    /// Verified real .NET: 2.25 / 1.5 / 5 / 2.50 (total keeps scale 2 from
    /// the 0.25-scaled loop values).
    /// </summary>
    [Test]
    [Category("Integration")]
    public void DecimalLiteral_ArgumentReturnForStep()
        => Assert.That(CompileRunCSharp(@"Module M
 Function Tax() As Decimal
  Return 1.5
 End Function
 Sub Show(d As Decimal)
  Console.WriteLine(d)
 End Sub
 Sub Main()
  Show(2.25)
  Console.WriteLine(Tax())
  Dim count As Integer = 0
  Dim total As Decimal = 0
  For d As Decimal = 0 To 1 Step 0.25
   count += 1
   total += d
  Next
  Console.WriteLine(count)
  Console.WriteLine(total)
 End Sub
End Module"), Is.EqualTo("2.25\n1.5\n5\n2.50"));

    /// <summary>
    /// CType is the named escape for genuine non-literal Double↔Decimal
    /// conversion, on the C# backend a plain cast in both directions
    /// ((decimal)x / (double)d — real .NET decimal has both explicit
    /// operators). Verified real .NET: both print 1.5 (double 1.5 is exactly
    /// representable, so the round trip is lossless).
    /// </summary>
    [Test]
    [Category("Integration")]
    public void CType_BothDirections_OnCSharp()
        => Assert.That(CompileRunCSharp(@"Module M
 Sub Main()
  Dim x As Double = 1.5
  Dim d As Decimal = CType(x, Decimal)
  Console.WriteLine(d)
  Dim y As Double = CType(d, Double)
  Console.WriteLine(y)
 End Sub
End Module"), Is.EqualTo("1.5\n1.5"));

    /// <summary>
    /// The IR constants for Decimal-context literals carry System.Decimal
    /// values, so the optimizer's int/long/float/double constant-fold patterns
    /// SKIP them (folding 0.1 + 0.2 in double space would yield
    /// 0.30000000000000004 and violate faithfulness on the optimizer-validated
    /// path). After the standard passes: no double constant may exist in this
    /// all-Decimal program; the add either survives to runtime or — if a
    /// future pass folds it — folded in decimal space to exactly 0.3m.
    /// </summary>
    [Test]
    public void Optimizer_DoesNotFoldDecimalInDoubleSpace()
    {
        var module = BuildOptimizedIR(Wrap(
            "Dim a As Decimal = 0.1\nDim b As Decimal = 0.2\nDim c As Decimal = a + b\nConsole.WriteLine(c)"));

        var constants = new List<object>();
        var addSurvives = false;
        foreach (var function in module.Functions)
            foreach (var block in function.Blocks)
                foreach (var inst in block.Instructions)
                {
                    CollectConstants(inst, constants);
                    if (inst is IRBinaryOp bin && bin.Operation == BinaryOpKind.Add)
                        addSurvives = true;
                }

        Assert.That(constants.OfType<double>(), Is.Empty,
            "an all-Decimal program leaked double constants into optimized IR (double-space conversion, spec 6.1)");
        Assert.That(constants.OfType<decimal>(), Is.Not.Empty,
            "expected System.Decimal IR constants for the Decimal-context literals");
        Assert.That(addSurvives || constants.OfType<decimal>().Contains(0.3m), Is.True,
            "the add op vanished without a faithful decimal result (0.3m)");
    }

    /// <summary>
    /// The compare fold must not touch decimal constants either: CompareLt/Gt
    /// know only int/long/float/double patterns and blindly report false for
    /// everything else, and TryFoldCompare folds UNCONDITIONALLY once both
    /// operands are constants — without a decimal guard, 0.1m &lt; 0.2m would
    /// fold to False (a miscompile; the truth is True). Hand-built IR because
    /// reaching this shape from source depends on which propagation pass runs
    /// first; the guard must hold regardless.
    /// </summary>
    [Test]
    public void Optimizer_DoesNotFoldDecimalComparisonBlindly()
    {
        var decType = new TypeInfo("Decimal", TypeKind.Class);
        var boolType = new TypeInfo("Boolean", TypeKind.Primitive);

        var fn = new IRFunction("f", boolType);
        var block = fn.CreateBlock("entry");
        block.Instructions.Add(new IRCompare("t0", CompareKind.Lt,
            new IRConstant(0.1m, decType), new IRConstant(0.2m, decType), boolType));

        var module = new IRModule("m");
        module.Functions.Add(fn);

        new ConstantFoldingPass().Run(module);

        var foldedTo = block.Instructions.OfType<IRConstant>().FirstOrDefault();
        Assert.That(foldedTo?.Value, Is.Not.EqualTo(false),
            "0.1m < 0.2m folded to False — the blind int/double compare fold ran on decimal constants");
        Assert.That(block.Instructions.OfType<IRCompare>().Any() || Equals(foldedTo?.Value, true), Is.True,
            "the compare vanished without a faithful result");
    }

    /// <summary>
    /// Full front-end pipeline through the standard optimizer passes (the same
    /// pipeline the CLI runs at -O1; cf. CppSelectCaseTests.CompileToCppOptimized).
    /// </summary>
    private static IRModule BuildOptimizedIR(string source)
    {
        var lexer = new Lexer(source);
        var parser = new Parser(lexer.Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));

        var module = new IRBuilder(analyzer).Build(ast, "TestModule");
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);
        return module;
    }

    /// <summary>
    /// Collects the boxed values of every IRConstant reachable from one
    /// instruction: the instruction itself (folds splice bare constants into
    /// the instruction list) plus the operand slots of the value-bearing
    /// instruction kinds (mirrors ConstantFoldingPass.ReplaceAllReferences).
    /// </summary>
    private static void CollectConstants(IRInstruction inst, List<object> constants)
    {
        void Add(IRValue? v)
        {
            if (v is IRConstant c && c.Value != null)
                constants.Add(c.Value);
        }

        if (inst is IRConstant self && self.Value != null)
            constants.Add(self.Value);

        switch (inst)
        {
            case IRBinaryOp bin: Add(bin.Left); Add(bin.Right); break;
            case IRUnaryOp un: Add(un.Operand); break;
            case IRCompare cmp: Add(cmp.Left); Add(cmp.Right); break;
            case IRAssignment asg: Add(asg.Value); break;
            case IRStore store: Add(store.Value); break;
            case IRReturn ret: Add(ret.Value); break;
            case IRConditionalBranch condBr: Add(condBr.Condition); break;
            case IRCall call: foreach (var a in call.Arguments) Add(a); break;
        }
    }

    // ====================================================================
    // C# backend smoke: integer-valued Decimal arithmetic end-to-end
    // (integral literals in Decimal contexts emit as 10m/3m constants).
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
