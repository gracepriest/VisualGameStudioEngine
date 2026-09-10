using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>AddressOf</c> — a function as a value — chips task_4392b185 (inferred local) and
/// task_e7c50371 (<c>List.Sort(AddressOf …)</c>).
///
/// <para><b>MEASURED before the fix, for <c>Dim f = AddressOf Greet : f()</c>:</b></para>
/// <code>
///   C#           Pointer To Void f = Greet;      CS1002
///   C++          'Pointer' was not declared
///   JavaScript   BL7007: 'Pointer To Void' is not available
/// </code>
///
/// <para>⛔ <b>The defect was in the ANALYZER, and every backend inherited it.</b>
/// <c>AddressOf f</c> was typed as <c>Pointer To &lt;f's return type&gt;</c> — a BasicLang
/// pointer type meant for C++ memory work — which discarded the parameter list and then leaked
/// as a type NAME into the generated code. It now denotes the delegate type the function has:
/// <c>Action(Of P…)</c> for a Sub, <c>Func(Of P…, R)</c> for a Function, built exactly as a
/// lambda's type is, so the two unify at every consumer. On JavaScript a function is already a
/// value, so <c>AddressOf Greet</c> is simply <c>Greet</c>.</para>
///
/// <para><b><c>List.Sort</c></b> lowers to <c>Array.prototype.sort</c>, whose comparator
/// contract matches .NET's <c>Comparison(Of T)</c>. Without a comparer JavaScript sorts
/// LEXICOGRAPHICALLY — <c>[10, 9, 1]</c> becomes <c>[1, 10, 9]</c> — so a numeric element type
/// gets a numeric comparator and an unknown one is refused.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node; the C# legs build with dotnet
public class JavaScriptAddressOfTests
{
    private static string Run(string source) => JavaScriptExecutionTests.RunJs(source);

    private const string GreetProgram =
        "Sub Greet()\nConsole.WriteLine(\"greeted\")\nEnd Sub\n" +
        "Sub Main()\nDim f = AddressOf Greet\nf()\nEnd Sub";

    private const string AddProgram =
        "Function Add(a As Integer, b As Integer) As Integer\nReturn a + b\nEnd Function\n" +
        "Sub Main()\nDim g = AddressOf Add\nConsole.WriteLine(g(2, 3))\nEnd Sub";

    // ---------------------------------------------------------------- the chip: inferred local

    [Test]
    public void AddressOfSub_InferredLocal_CanBeCalled_JavaScript()
        => Assert.That(Run(GreetProgram), Is.EqualTo("greeted"));

    [Test]
    public void AddressOfFunction_InferredLocal_CanBeCalledWithArguments_JavaScript()
        => Assert.That(Run(AddProgram), Is.EqualTo("5"));

    /// <summary>The C# mirror — CS1002 before; the fix is in the analyzer, so this is the proof.</summary>
    [Test]
    public void AddressOfSub_InferredLocal_CanBeCalled_CSharp()
        => Assert.That(CliTestHarness.CompileRunCSharp(GreetProgram).Trim(), Is.EqualTo("greeted"));

    [Test]
    public void AddressOfFunction_InferredLocal_CanBeCalledWithArguments_CSharp()
        => Assert.That(CliTestHarness.CompileRunCSharp(AddProgram).Trim(), Is.EqualTo("5"));

    // ---------------------------------------------------------------- declared delegate types

    [Test]
    public void AddressOf_AssignedToADeclaredAction()
        => Assert.That(Run(
            "Sub Greet()\nConsole.WriteLine(\"hi\")\nEnd Sub\n" +
            "Sub Main()\nDim f As Action = AddressOf Greet\nf()\nEnd Sub"),
            Is.EqualTo("hi"));

    [Test]
    public void AddressOf_AssignedToADeclaredFunc()
        => Assert.That(Run(
            "Function Twice(x As Integer) As Integer\nReturn x * 2\nEnd Function\n" +
            "Sub Main()\nDim f As Func(Of Integer, Integer) = AddressOf Twice\nConsole.WriteLine(f(21))\nEnd Sub"),
            Is.EqualTo("42"));

    /// <summary>Passed as an argument where a lambda would go — the two must be interchangeable.</summary>
    [Test]
    public void AddressOf_PassedToAParameterOfDelegateType()
        => Assert.That(Run(
            "Function Twice(x As Integer) As Integer\nReturn x * 2\nEnd Function\n" +
            "Sub Apply(f As Func(Of Integer, Integer), v As Integer)\nConsole.WriteLine(f(v))\nEnd Sub\n" +
            "Sub Main()\nApply(AddressOf Twice, 4)\nApply(Function(x As Integer) x + 1, 4)\nEnd Sub"),
            Is.EqualTo("8\n5"));

    // ---------------------------------------------------------------- the chip: List.Sort

    private const string SortProgram =
        "Function Desc(a As Integer, b As Integer) As Integer\n" +
        "If a > b Then\nReturn -1\nEnd If\nIf a < b Then\nReturn 1\nEnd If\nReturn 0\nEnd Function\n" +
        "Sub Main()\nDim l As New List(Of Integer)()\nl.Add(2)\nl.Add(3)\nl.Add(1)\n" +
        "l.Sort(AddressOf Desc)\nFor Each n As Integer In l\nConsole.WriteLine(n)\nNext\nEnd Sub";

    [Test]
    public void ListSort_WithAnAddressOfComparer_JavaScript()
        => Assert.That(Run(SortProgram), Is.EqualTo("3\n2\n1"));

    [Test]
    public void ListSort_WithAnAddressOfComparer_CSharp()
        => Assert.That(CliTestHarness.CompileRunCSharp(SortProgram).Replace("\r\n", "\n").Trim(), Is.EqualTo("3\n2\n1"));

    /// <summary>THE lexicographic trap: 10 must not sort before 9.</summary>
    [Test]
    public void ListSort_NoComparer_SortsNumbersNumerically()
        => Assert.That(Run(
            "Sub Main()\nDim l As New List(Of Integer)()\nl.Add(10)\nl.Add(9)\nl.Add(1)\nl.Sort()\n" +
            "For Each n As Integer In l\nConsole.WriteLine(n)\nNext\nEnd Sub"),
            Is.EqualTo("1\n9\n10"));

    [Test]
    public void ListSort_NoComparer_SortsStrings()
        => Assert.That(Run(
            "Sub Main()\nDim l As New List(Of String)()\nl.Add(\"pear\")\nl.Add(\"apple\")\nl.Sort()\n" +
            "For Each s As String In l\nConsole.WriteLine(s)\nNext\nEnd Sub"),
            Is.EqualTo("apple\npear"));

    // ---------------------------------------------------------------- the SHIPPING IR

    [Test]
    public void Optimized_AddressOf_InferredLocal()
        => Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(AddProgram), Is.EqualTo("5"));

    [Test]
    public void Optimized_ListSort_WithComparer()
        => Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(SortProgram), Is.EqualTo("3\n2\n1"));
}

/// <summary>Codegen-side: the pointer type must never reach any backend's output.</summary>
[TestFixture]
public class AddressOfTypingTests
{
    private const string Program =
        "Sub Greet()\nConsole.WriteLine(\"greeted\")\nEnd Sub\n" +
        "Sub Main()\nDim f = AddressOf Greet\nf()\nEnd Sub";

    [Test]
    public void JavaScript_EmitsTheFunctionReference()
    {
        var js = JsTestSupport.Compile(Program);

        // The reference, not a call: `= Greet;` (possibly through an SSA temp), never `Greet()` here.
        Assert.That(js, Does.Contain("= Greet;"));
        Assert.That(js, Does.Not.Contain("Pointer"));
    }

    [Test]
    public void CSharp_DeclaresAnAction_NotAPointer()
    {
        var cs = new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator().Generate(JsTestSupport.BuildModule(Program));

        Assert.That(cs, Does.Not.Contain("Pointer To"), "the analyzer's pointer type leaked as a C# type name");
        Assert.That(cs, Does.Contain("Action"));
    }

    [Test]
    public void Cpp_DeclaresAStdFunction_NotAPointer()
    {
        var cpp = BclE2E.CompileToCppOptimized(Program);

        Assert.That(cpp, Does.Not.Contain("Pointer To"));
        Assert.That(cpp, Does.Contain("std::function<void()>"));
    }

    /// <summary>A Function's delegate carries its parameter AND return types.</summary>
    [Test]
    public void AddressOfFunction_IsAFuncOfItsSignature()
    {
        var cs = new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator().Generate(JsTestSupport.BuildModule(
            "Function Add(a As Integer, b As Integer) As Integer\nReturn a + b\nEnd Function\n" +
            "Sub Main()\nDim g = AddressOf Add\nConsole.WriteLine(g(2, 3))\nEnd Sub"));

        Assert.That(cs, Does.Contain("Func<int, int, int> g"));
    }

    /// <summary>Sorting an element type with no default ordering is refused, not silently lexicographic.</summary>
    [Test]
    public void ListSort_NoComparer_OnAnUnorderedElementType_IsRefused()
        => Assert.That(() => JsTestSupport.Compile(
                "Class P\nPublic X As Integer\nEnd Class\n" +
                "Sub Main()\nDim l As New List(Of P)()\nl.Sort()\nEnd Sub"),
            Throws.TypeOf<System.NotSupportedException>());
}
