using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Module-level <c>Dim</c> — a variable declared outside any Sub, shared by every function.
///
/// <para><b>MEASURED before the fix.</b> The JavaScript backend never emitted
/// <c>module.GlobalVariables</c> at all, and a function's <c>n = n + 1</c> came out as
/// <c>const n = ((n + 1) | 0);</c> — IRBuilder renames the operation's result to the variable
/// it assigns, and the backend's <c>Bind</c> knew only locals and class members, so an unknown
/// name became a fresh <c>const</c>. Node died with "Cannot access 'n' before initialization"
/// from a build that reported success.</para>
///
/// <para>The lowering is a top-level <c>let</c> per global, emitted BEFORE classes and functions
/// (a class's static initialiser may read one), with globals treated as declared names inside
/// every function so an assignment is an assignment.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptModuleVariableTests
{
    private static string Run(string source) => JavaScriptExecutionTests.RunJs(source);

    private const string Shared =
        "Dim n As Integer\n" +
        "Function Tick() As Integer\nn = n + 1\nReturn n\nEnd Function\n" +
        "Sub Main()\nn = 10\nTick()\nTick()\nConsole.WriteLine(n)\nEnd Sub";

    /// <summary>THE case: one variable, written in one function and read in another.</summary>
    [Test]
    public void ModuleLevelDim_IsSharedAcrossFunctions()
        => Assert.That(Run(Shared), Is.EqualTo("12"));

    [Test]
    public void ModuleLevelDim_DefaultsToZero()
        => Assert.That(Run("Dim n As Integer\nSub Main()\nConsole.WriteLine(n)\nEnd Sub"), Is.EqualTo("0"));

    [Test]
    public void ModuleLevelDim_WithAConstantInitializer()
        => Assert.That(Run("Dim n As Integer = 5\nDim s As String = \"hi\"\nSub Main()\nConsole.WriteLine(n)\nConsole.WriteLine(s)\nEnd Sub"),
            Is.EqualTo("5\nhi"));

    [Test]
    public void ModuleLevelDim_IsVisibleInsideAClassMethod()
        => Assert.That(Run(
            "Dim prefix As String = \"> \"\n" +
            "Class Printer\nPublic Sub Say(msg As String)\nConsole.WriteLine(prefix & msg)\nEnd Sub\nEnd Class\n" +
            "Sub Main()\nDim p As New Printer()\np.Say(\"hello\")\nEnd Sub"),
            Is.EqualTo("> hello"));

    /// <summary>A module-level array is SIZED at its declaration, like a local one (ArrayInitializer's third caller).</summary>
    [Test]
    public void ModuleLevelArray_IsSizedAndShared()
        => Assert.That(Run(
            "Dim g(2) As Integer\n" +
            "Sub Fill()\ng(1) = 7\nEnd Sub\n" +
            "Sub Main()\nFill()\nConsole.WriteLine(g(1))\nEnd Sub"),
            Is.EqualTo("7"));

    [Test]
    public void ModuleLevelDim_ReadAndWrittenInALoop()
        => Assert.That(Run(
            "Dim total As Integer\n" +
            "Sub Main()\nFor i As Integer = 1 To 4\ntotal = total + i\nNext\nConsole.WriteLine(total)\nEnd Sub"),
            Is.EqualTo("10"));

    [Test]
    public void Optimized_ModuleLevelDim_IsSharedAcrossFunctions()
        => Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(Shared), Is.EqualTo("12"));
}

/// <summary>Codegen-side contract, no Node.</summary>
[TestFixture]
public class JavaScriptModuleVariableCodeGenTests
{
    [Test]
    public void ModuleLevelDim_IsATopLevelLet_BeforeAnyFunction()
    {
        var js = JsTestSupport.Compile("Dim n As Integer = 5\nSub Main()\nConsole.WriteLine(n)\nEnd Sub");

        var decl = js.IndexOf("let n = 5;", System.StringComparison.Ordinal);
        var fn = js.IndexOf("function Main", System.StringComparison.Ordinal);

        Assert.That(decl, Is.GreaterThanOrEqualTo(0), "no top-level declaration");
        Assert.That(fn, Is.GreaterThanOrEqualTo(0));
        Assert.That(decl, Is.LessThan(fn));
    }

    /// <summary>Inside a function the global is ASSIGNED, never re-declared as a const.</summary>
    [Test]
    public void AssignmentToAGlobal_IsAnAssignment()
    {
        var js = JsTestSupport.Compile("Dim n As Integer\nSub Bump()\nn = n + 1\nEnd Sub\nSub Main()\nBump()\nEnd Sub");

        Assert.That(js, Does.Not.Contain("const n"));
        Assert.That(js, Does.Contain("n = "));
    }

    /// <summary>Globals precede classes: a static field initialiser may read one.</summary>
    [Test]
    public void ModuleLevelDim_PrecedesClasses()
    {
        var js = JsTestSupport.Compile(
            "Dim n As Integer = 1\nClass C\nPublic X As Integer\nEnd Class\nSub Main()\nConsole.WriteLine(n)\nEnd Sub");

        Assert.That(js.IndexOf("let n = 1;", System.StringComparison.Ordinal),
            Is.LessThan(js.IndexOf("class C", System.StringComparison.Ordinal)));
    }
}
