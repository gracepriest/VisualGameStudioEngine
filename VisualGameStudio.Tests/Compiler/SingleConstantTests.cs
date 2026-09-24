using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A numeric literal initialises a <c>Const</c> the way it initialises a <c>Dim</c>. MEASURED on
/// master 15e4e63: <c>Const X As Single = 2.5</c> (module or local, and <c>-2.5</c>) and
/// <c>Const D As Decimal = 2.5</c> were rejected — "Constant value type 'Double' is not compatible
/// with declared type 'Single'" — while <c>Dim x As Single = 2.5</c> compiled, so only a Const
/// needed an <c>F</c> suffix. Once accepted, a LOCAL Single Const then emitted <c>L = 0.5;</c>
/// into a float on C# (CS0664), because its initializer skipped the coercion a local Dim gets.
/// Everything here goes through the optimizer.
/// </summary>
public class SingleConstantTests
{
    internal const string SingleProgram = @"
Const Half As Single = 2.5
Const Neg As Single = -1.25
Const Whole As Single = 3
Sub Check(tag As String, ok As Boolean)
    If ok Then
        Console.WriteLine(tag & "" ok"")
    Else
        Console.WriteLine(tag & "" BAD"")
    End If
End Sub
Sub Main()
    Const Local As Single = 0.5
    Dim f As Single = Half * 2
    Check(""half"", f = 5.0F)
    Check(""neg"", Neg + 1.25F = 0.0F)
    Check(""whole"", Whole / 2 = 1.5F)
    Check(""local"", Local + Half = 3.0F)
End Sub
";

    internal const string SingleExpected = "half ok\nneg ok\nwhole ok\nlocal ok";

    internal const string DecimalProgram = @"
Const Rate As Decimal = 2.5
Sub Main()
    Const Fee As Decimal = 0.1
    Dim total As Decimal = Rate * 4 + Fee
    If total = 10.1 Then
        Console.WriteLine(""dec ok"")
    Else
        Console.WriteLine(""dec BAD"")
    End If
End Sub
";

    private static string[] SemanticErrors(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty);
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.Select(e => e.Message).ToArray();
    }

    private static string WithMain(string decl) => decl + "\nSub Main()\n    Console.WriteLine(CStr(X))\nEnd Sub\n";

    [TestCase("Const X As Single = 2.5")]
    [TestCase("Const X As Single = -2.5")]
    [TestCase("Const X As Single = 3")]
    [TestCase("Const X As Decimal = 2.5")]
    public void DoubleLiteral_InitialisesANumericConst(string decl) =>
        Assert.That(SemanticErrors(WithMain(decl)), Is.Empty);

    /// <summary>The literal rule is not a blanket pass: range and type are still checked.</summary>
    [TestCase("Const X As Single = 1.0E40", "not representable in type 'Single'")]
    [TestCase("Const X As Byte = 300", "not representable in type 'Byte'")]
    [TestCase("Const X As String = 2.5", "not compatible with declared type 'String'")]
    [TestCase("Const X As Boolean = 2.5", "not compatible with declared type 'Boolean'")]
    public void StillRejected(string decl, string expected) =>
        Assert.That(SemanticErrors(WithMain(decl)), Has.Some.Contains(expected));

    [Test]
    public void CSharp_EmitsFloatAndDecimalConstants_AndCompiles()
    {
        var single = ReturnCoercionTests.EmitCSharpForTest(SingleProgram);
        var dec = ReturnCoercionTests.EmitCSharpForTest(DecimalProgram);
        Assert.Multiple(() =>
        {
            Assert.That(single, Does.Contain("const float Half = 2.5f;"));
            Assert.That(single, Does.Contain("const float Neg = -1.25f;"));
            Assert.That(single, Does.Contain("Local = 0.5f;"), "the local Const was CS0664 `Local = 0.5;`");
            Assert.That(dec, Does.Contain("const decimal Rate = 2.5m;"));
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(SingleProgram), Is.Empty);
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(DecimalProgram), Is.Empty);
        });
    }
}

/// <summary>The same programs compiled AND run. JavaScript refuses Decimal by design (BL7007).</summary>
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class SingleConstantRunTests
{
    [Test]
    public void CSharp_Runs() => Assert.Multiple(() =>
    {
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(SingleConstantTests.SingleProgram)),
            Is.EqualTo(SingleConstantTests.SingleExpected));
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(SingleConstantTests.DecimalProgram)),
            Is.EqualTo("dec ok"));
    });

    [Test]
    public void Cpp_Runs() => Assert.Multiple(() =>
    {
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(SingleConstantTests.SingleProgram))),
            Is.EqualTo(SingleConstantTests.SingleExpected));
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(SingleConstantTests.DecimalProgram))),
            Is.EqualTo("dec ok"));
    });

    [Test]
    public void JavaScript_Runs() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(SingleConstantTests.SingleProgram))),
            Is.EqualTo(SingleConstantTests.SingleExpected));
}
