using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// CType/DirectCast conversion legality — the root cause behind chip task_0c803e75.
///
/// SemanticAnalyzer.Visit(CastExpressionNode) visited the operand, resolved the target, checked
/// TryCast, and set the node type. It NEVER READ THE SOURCE TYPE. So a cast between any two
/// types type-checked, and the breakage surfaced downstream — as csc CS0030, as a g++
/// static_cast error, or worst of all as a silently wrong value.
///
/// The chip reported this as an enum problem. It is not: measured, a user-defined class with
/// no .NET or enum involvement anywhere reproduces it identically —
/// <c>CType(w, Integer)</c> emits <c>(int)(w)</c> on C# and <c>static_cast&lt;int32_t&gt;(w)</c>
/// on C++, and BOTH compilers reject it.
///
/// ⛔ THE WORST ROW IS THE SILENT ONE. Any reference cast to Boolean COMPILES AND RUNS on the
/// C++ backend, because a handle type has an <c>explicit operator bool()</c> and the cast
/// binds to it as an exact match. The program prints handle-truthiness — True for a
/// zero-valued member where VB requires False. No "does the generated C++ compile" gate can
/// ever catch that one, which is why the check belongs in the FRONT END.
///
/// SCOPE, DELIBERATE. Only reference-to-scalar and scalar-to-reference are refused here.
/// Enum-to-integral is a REAL VB conversion, is already correct on the C# backend, and is the
/// chip's literal repro on the native backend — that row is left to the enum lowering work
/// (P2a-2 T8c-3), which fixes it by making the operand a constant rather than by refusing it.
/// Unrelated class-to-class is also broken but is not touched here: inheritance, interfaces
/// and generics make that a materially different judgement.
/// </summary>
[TestFixture]
public class CastLegalityTests
{
    private static List<string> Analyze(string source)
    {
        var errors = new List<string>();
        var lexer = new Lexer(source);
        var parser = new Parser(lexer.Tokenize());

        ProgramNode ast;
        try
        {
            ast = parser.Parse();
        }
        catch (Exception ex)
        {
            errors.Add($"Parse error: {ex.Message}");
            return errors;
        }

        var analyzer = new SemanticAnalyzer();
        if (!analyzer.Analyze(ast))
            foreach (var err in analyzer.Errors)
                errors.Add(err.Message);

        return errors;
    }

    private const string WidgetClass = @"
Class Widget
    Public Sub Poke()
    End Sub
End Class
";

    // ========================================================================
    // Refused
    // ========================================================================

    [Test]
    public void CastingAClassReferenceToANumber_IsRefused()
    {
        var errors = Analyze(WidgetClass + @"
Sub Main()
    Dim w As Widget = New Widget()
    Dim n As Integer = CType(w, Integer)
    Console.WriteLine(n)
End Sub
");
        Assert.That(errors, Is.Not.Empty,
            "there is no conversion from a class reference to Integer; both csc and g++ " +
            "reject the emitted code, so the compiler must refuse it first");
        Assert.That(string.Join(" | ", errors), Does.Contain("Widget"));
    }

    [Test]
    public void CastingAClassReferenceToBoolean_IsRefused()
    {
        // THE SILENT ROW. On C++ this compiles AND RUNS, yielding handle-truthiness instead
        // of a value — the one row no downstream compiler check could ever catch.
        var errors = Analyze(WidgetClass + @"
Sub Main()
    Dim w As Widget = New Widget()
    Dim b As Boolean = CType(w, Boolean)
    Console.WriteLine(b)
End Sub
");
        Assert.That(errors, Is.Not.Empty,
            "a reference cast to Boolean binds to NetRef's explicit operator bool() on C++ " +
            "and silently reports whether the HANDLE is non-null");
    }

    [Test]
    public void CastingANumberToAClassReference_IsRefused()
    {
        var errors = Analyze(WidgetClass + @"
Sub Main()
    Dim w As Widget = CType(3, Widget)
    w.Poke()
End Sub
");
        Assert.That(errors, Is.Not.Empty,
            "scalar-to-reference has no conversion either; csc reports CS0030");
    }

    // ========================================================================
    // Must stay legal — the regression gate
    // ========================================================================

    [Test]
    [TestCase("Dim n As Integer = CType(3.7, Integer)", TestName = "Double to Integer")]
    [TestCase("Dim d As Double = CType(3, Double)", TestName = "Integer to Double")]
    [TestCase("Dim s As String = CType(3, String)", TestName = "Integer to String")]
    [TestCase("Dim n As Integer = CType(\"42\", Integer)", TestName = "String to Integer")]
    [TestCase("Dim b As Boolean = CType(1, Boolean)", TestName = "Integer to Boolean")]
    public void ConversionsThatAreRealRemainLegal(string statement)
    {
        var errors = Analyze($@"
Sub Main()
    {statement}
End Sub
");
        Assert.That(errors, Is.Empty,
            "narrowing the check must not narrow the language — these are genuine VB " +
            "conversions and every one was measured green before the check existed");
    }

    [Test]
    public void CastingAnInterfaceToItsImplementation_RemainsLegal()
    {
        var errors = Analyze(@"
Interface IPoker
    Sub Poke()
End Interface

Class Widget
    Implements IPoker
    Public Sub Poke() Implements IPoker.Poke
    End Sub
End Class

Sub Main()
    Dim i As IPoker = New Widget()
    Dim w As Widget = CType(i, Widget)
    w.Poke()
End Sub
");
        Assert.That(errors, Is.Empty,
            "reference-to-reference is exactly what CType is FOR; only the " +
            "reference/scalar boundary is refused");
    }

    [Test]
    public void CastingThroughObject_RemainsLegal()
    {
        // Object is the universal box and VB permits CType in both directions. It is also
        // how a .NET enum member types on the C# backend path, so refusing it here would
        // regress a currently-working program.
        var errors = Analyze(@"
Sub Main()
    Dim o As Object = 42
    Dim n As Integer = CType(o, Integer)
    Console.WriteLine(n)
End Sub
");
        Assert.That(errors, Is.Empty);
    }
}
