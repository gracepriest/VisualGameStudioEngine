using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A FILE-SCOPE procedure, global or constant used from anywhere outside the file module's own
/// static class on the C# backend: a class method, a constructor, a property getter, a Shared
/// method, a lambda in a method, a <c>Module</c> block's procedure — and, unchanged, from
/// <c>Main</c> and from another file-scope procedure, where it is spelled bare.
///
/// <para>⛔ Every one of those was CS0103 on C# alone, measured, while C++, JavaScript and MSIL
/// ran the same program. Two causes, both fixed here. The IR builder gave a file-scope callee NO
/// owner (<c>IRCall.CalleeModule</c> was null; a Module's procedure carried its Module), so the
/// one backend that keeps each module in its own static class had nothing to qualify by. And
/// that backend decided "bare or qualified" by comparing MODULE NAMES — the member's against the
/// emitting function's — when a class's methods carry the file module's name too, so a file-scope
/// name from a class body compared equal and went out bare inside a C# class that has no such
/// member. A Module block's procedure is a different static class, same bare spelling.</para>
///
/// <para>⚠ The fix: the IR builder stamps a file-scope callee with the file's module as owner
/// and spells it under that owner exactly as it DECLARED it (<see cref="AFileScopeFunction_ContestedWithAModulesProcedure_RunsOnEveryBackend"/>:
/// the declaration was already <c>Main_F</c> when a Module had an <c>F</c> too, and the call
/// was the bare <c>F</c> — a call to a function no backend defined, broken on all four). The C#
/// backend records WHICH module's static class it is writing into and qualifies unless the
/// reference lands in that class. A class's own method — declared above or below its caller,
/// inherited, or sharing a name with a file-scope function — stays bare: the analyzer flattens
/// every method signature into the global scope by bare name, so such a call arrives bound to a
/// global-scope symbol, and the builder asks the class type (complete after analysis) before
/// calling anything file scope; every backend resolves the bare spelling to the member.</para>
///
/// <para>⛔ A THIRD DEFECT, unmasked by qualification: a file-scope <c>Dim</c> or <c>Const</c>
/// is Private by default and was emitted <c>private static</c> in the file's class, so
/// <c>Program.Total</c> from a class or a Module block was CS0122 the moment it was spelled
/// right (a Module block reading a file-scope global already failed that way, measured before
/// the change). A file-scope Private is private to its FILE and the front end enforces that; a
/// module's static-class members now map Public → <c>public</c>, else <c>internal</c>, as MSIL
/// already does (<c>assembly</c>). Class members are untouched.</para>
///
/// <para>⚠ Every case with no recorded gap asserts all four backends, compiled and run through
/// the optimizer. Where JavaScript, MSIL or C++ has its own recorded gap (ByRef; <c>Func</c>;
/// a class-returning callee; a ReadOnly property; an inherited method on MSIL), the case runs
/// on the others and names the gap. A <c>Declare</c> from a class body is CS0103 still —
/// externs are emitted into whichever module class comes first, so no owner is right for them;
/// out of scope, pinned by the wire-form test.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out; the multi-file leg writes temp files
public class CsFileScopeQualificationTests
{
    private static string Norm(string s) => FourBackends.Norm(s);
    private static void RunsOnEveryBackend(string program, string expected) => FourBackends.RunsOnEveryBackend(program, expected);
    private static string Cpp(string program) => Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)));
    private static string Js(string program) => Norm(JavaScriptExecutionTests.RunJs(program));
    private static string Msil(string program) => Norm(MsilHarness.RunExpectingSuccess(program));
    private static string Cs(string program) => Norm(FourBackends.RunEmittedCSharp(program));
    private static string CsText(string program) => ReturnCoercionTests.EmitCSharpForTest(program);

    /// <summary>The file module's static class in <see cref="ReturnCoercionTests.EmitCSharpForTest"/>'s output.</summary>
    private const string FileClass = "ReturnCoercionProbe";

    private const string FnTwice = "Function Twice(n As Integer) As Integer\n Return n * 2\nEnd Function\n";
    // Leading newline: a raw string literal ends without one, and `End Class` + `Sub Main()` is one line.
    private const string MainBox = "\nSub Main()\n Dim b As New Box()\n PrintLine(CStr(b.Run()))\nEnd Sub\n";

    // ------------------------------------------------------------------ from a class body

    /// <summary>The headline, with the spelling that makes it compile.</summary>
    [Test]
    public void AClassMethod_CallingAFileScopeFunction_IsQualifiedByTheFileClass_AndRunsOnEveryBackend()
    {
        var program = FnTwice + "Class Box\n Public Function Run() As Integer\n  Return Twice(4)\n End Function\nEnd Class\n" + MainBox;
        Assert.Multiple(() =>
        {
            Assert.That(CsText(program), Does.Contain(FileClass + ".Twice(4)"), "qualified by the file module's class");
            RunsOnEveryBackend(program, "8");
        });
    }

    [Test]
    public void AConstructor_CallingAFileScopeFunction_RunsOnEveryBackend()
        => RunsOnEveryBackend(FnTwice + """
            Class Box
             Public V As Integer
             Public Sub New()
              Dim t As Integer = Twice(21)
              V = t
             End Sub
             Public Function Run() As Integer
              Return V
             End Function
            End Class
            """ + MainBox, "42");

    /// <summary>
    /// A getter body. ⚠ C++ has its own recorded gap here — a ReadOnly Property is not reachable
    /// as a member (<c>CppEmissionOrderTests</c> pins it) — so this runs on the other three.
    /// </summary>
    [Test]
    public void APropertyGetter_CallingAFileScopeFunction_RunsOnThree()
    {
        var program = FnTwice + """
            Class Box
             Public ReadOnly Property Doubled As Integer
              Get
               Return Twice(6)
              End Get
             End Property
             Public Function Run() As Integer
              Return Doubled
             End Function
            End Class
            """ + MainBox;
        Assert.Multiple(() =>
        {
            Assert.That(Js(program), Is.EqualTo("12"), "JavaScript");
            Assert.That(Msil(program), Is.EqualTo("12"), "MSIL");
            Assert.That(Cs(program), Is.EqualTo("12"), "C#");
        });
    }

    [Test]
    public void ASharedMethod_CallingAFileScopeFunction_RunsOnEveryBackend()
        => RunsOnEveryBackend(FnTwice + """
            Class Box
             Public Shared Function Run() As Integer
              Return Twice(8)
             End Function
            End Class
            Sub Main()
             PrintLine(CStr(Box.Run()))
            End Sub
            """, "16");

    /// <summary>The statement form: a Sub call is emitted from a different site than an expression call.</summary>
    [Test]
    public void AClassMethod_CallingAFileScopeSub_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Sub Say(s As String)
             PrintLine(s)
            End Sub
            Class Box
             Public Sub Run()
              Say("a")
             End Sub
            End Class
            Sub Main()
             Dim b As New Box()
             b.Run()
            End Sub
            """, "a");

    /// <summary>The omitted Optional is still filled at the call once the target is qualified.</summary>
    [Test]
    public void AClassMethod_CallingAFileScopeFunction_WithAnOptionalParameter_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Function Add(a As Integer, Optional b As Integer = 5) As Integer
             Return a + b
            End Function
            Class Box
             Public Function Run() As Integer
              Return Add(1) + Add(1, 1)
             End Function
            End Class
            """ + MainBox, "8");

    /// <summary>ByRef keeps its marker through the qualified spelling. JavaScript and MSIL have their own recorded ByRef gaps.</summary>
    [Test]
    public void AClassMethod_CallingAFileScopeSubByRef_RunsOnCppAndCSharp()
    {
        var program = """
            Sub Bump(ByRef n As Integer)
             n = n + 1
            End Sub
            Class Box
             Public Function Run() As Integer
              Dim x As Integer = 4
              Bump(x)
              Return x
             End Function
            End Class
            """ + MainBox;
        Assert.Multiple(() =>
        {
            Assert.That(Cpp(program), Is.EqualTo("5"), "C++");
            Assert.That(Cs(program), Is.EqualTo("5"), "C#");
        });
    }

    /// <summary>
    /// A lambda inside a method is still inside the class. MSIL has no <c>Func</c> yet (recorded).
    /// </summary>
    [Test]
    public void ALambdaInAClassMethod_CallingAFileScopeFunction_RunsOnThree()
    {
        var program = FnTwice + """
            Class Box
             Public Function Run() As Integer
              Dim f As Func(Of Integer, Integer) = Function(x As Integer) Twice(x)
              Return f(4)
             End Function
            End Class
            """ + MainBox;
        Assert.Multiple(() =>
        {
            Assert.That(Cpp(program), Is.EqualTo("8"), "C++");
            Assert.That(Js(program), Is.EqualTo("8"), "JavaScript");
            Assert.That(Cs(program), Is.EqualTo("8"), "C#");
        });
    }

    /// <summary>A callee returning a class, declared AFTER the class that calls it. MSIL's assembler rejects the shape (recorded).</summary>
    [Test]
    public void AClassMethod_CallingAFileScopeFunctionReturningAClass_RunsOnThree()
    {
        const string program = """
            Class Box
             Public V As Integer
             Public Function Twin() As Box
              Dim c As Box = Make(V + 1)
              Return c
             End Function
            End Class
            Function Make(v As Integer) As Box
             Dim c As New Box()
             c.V = v
             Return c
            End Function
            Sub Main()
             Dim b As New Box()
             b.V = 4
             PrintLine(CStr(b.Twin().V))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(Cpp(program), Is.EqualTo("5"), "C++");
            Assert.That(Js(program), Is.EqualTo("5"), "JavaScript");
            Assert.That(Cs(program), Is.EqualTo("5"), "C#");
        });
    }

    // ------------------------------------------------------------------ file-scope globals and constants

    /// <summary>⛔ The read: qualified, AND reachable — the third defect's <c>internal</c>.</summary>
    [Test]
    public void AClassMethod_ReadingAFileScopeGlobal_IsQualifiedAndInternal_AndRunsOnEveryBackend()
    {
        const string program = """
            Dim Total As Integer = 7
            Class Box
             Public Function Run() As Integer
              Return Total
             End Function
            End Class
            """ + MainBox;
        Assert.Multiple(() =>
        {
            var cs = CsText(program);
            Assert.That(cs, Does.Contain(FileClass + ".Total"), "qualified by the file module's class");
            Assert.That(cs, Does.Contain("internal static int Total = 7;"), "a file-scope Private is the file's, not the class's");
            RunsOnEveryBackend(program, "7");
        });
    }

    /// <summary>
    /// ⛔ The write. A destination carries only the NAME, not the variable, so it is qualified
    /// through a separate lookup (<c>GlobalNamedBy</c>); left bare, it was CS0103 on its own.
    /// </summary>
    [Test]
    public void AClassMethod_WritingAFileScopeGlobal_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Dim Total As Integer = 1
            Class Box
             Public Sub Bump()
              Total = Total + 10
             End Sub
            End Class
            Sub Main()
             Dim b As New Box()
             b.Bump()
             PrintLine(CStr(Total))
            End Sub
            """, "11");

    [Test]
    public void AClassMethod_ReadingAFileScopeConst_IsInternal_AndRunsOnEveryBackend()
    {
        const string program = """
            Const Limit As Integer = 9
            Class Box
             Public Function Run() As Integer
              Return Limit
             End Function
            End Class
            """ + MainBox;
        Assert.Multiple(() =>
        {
            Assert.That(CsText(program), Does.Contain("internal const int Limit = 9;"));
            RunsOnEveryBackend(program, "9");
        });
    }

    [Test]
    public void AClassMethod_IndexingAFileScopeArray_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Dim Vals(3) As Integer
            Class Box
             Public Function Run() As Integer
              Return Vals(1)
             End Function
            End Class
            Sub Main()
             Vals(1) = 8
             Dim b As New Box()
             PrintLine(CStr(b.Run()))
            End Sub
            """, "8");

    /// <summary>A global OF the class's own type, declared above the class, read from inside it.</summary>
    [Test]
    public void AClassMethod_ReadingAFileScopeGlobalOfItsOwnClassType_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Dim Shared1 As Box
            Class Box
             Public V As Integer
             Public Function Peer() As Integer
              Return Shared1.V
             End Function
            End Class
            Sub Main()
             Shared1 = New Box()
             Shared1.V = 6
             Dim b As New Box()
             PrintLine(CStr(b.Peer()))
            End Sub
            """, "6");

    // ------------------------------------------------------------------ from a Module block

    /// <summary>A Module block is its own static class; the file-scope callee is declared before or after it.</summary>
    [TestCase(true, TestName = "{m}(declared before the Module)")]
    [TestCase(false, TestName = "{m}(declared after the Module)")]
    public void AModuleProcedure_CallingAFileScopeFunction_RunsOnEveryBackend(bool declaredBefore)
    {
        const string mod = "Module M\n Public Function Four() As Integer\n  Return Twice(2)\n End Function\nEnd Module\n";
        const string main = "Sub Main()\n PrintLine(CStr(M.Four()))\nEnd Sub\n";
        var program = declaredBefore ? FnTwice + mod + main : mod + FnTwice + main;
        Assert.Multiple(() =>
        {
            Assert.That(CsText(program), Does.Contain(FileClass + ".Twice(2)"), "qualified inside static class M");
            RunsOnEveryBackend(program, "4");
        });
    }

    /// <summary>
    /// ⛔ This one was ALREADY qualified before the change and failed anyway — CS0122: the
    /// file-scope <c>Dim</c> was <c>private static</c> in the file's class. The third defect,
    /// on its own.
    /// </summary>
    [Test]
    public void AModulesMain_ReadingAFileScopeGlobal_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Dim Total As Integer = 7
            Module M
             Sub Main()
              PrintLine(CStr(Total))
             End Sub
            End Module
            """, "7");

    [Test]
    public void AModulesMain_WritingAFileScopeGlobal_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Dim Total As Integer = 1
            Module M
             Sub Main()
              Total = Total + 10
              PrintLine(CStr(Total))
             End Sub
            End Module
            """, "11");

    // ------------------------------------------------------------------ declared Private at file scope

    /// <summary>
    /// An EXPLICIT Private file-scope function is the file's: reachable from its classes and
    /// Module blocks on every backend, so it maps to <c>internal</c> too.
    /// </summary>
    [Test]
    public void APrivateFileScopeFunction_FromAClassMethod_IsInternal_AndRunsOnEveryBackend()
    {
        const string program = """
            Private Function Twice(n As Integer) As Integer
             Return n * 2
            End Function
            Class Box
             Public Function Run() As Integer
              Return Twice(4)
             End Function
            End Class
            """ + MainBox;
        Assert.Multiple(() =>
        {
            Assert.That(CsText(program), Does.Contain("internal static int Twice("));
            RunsOnEveryBackend(program, "8");
        });
    }

    [Test]
    public void APrivateFileScopeFunction_FromAModulesMain_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Private Function Twice(n As Integer) As Integer
             Return n * 2
            End Function
            Module M
             Sub Main()
              PrintLine(CStr(Twice(4)))
             End Sub
            End Module
            """, "8");

    [Test]
    public void APrivateFileScopeConst_FromAModulesMain_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Private Const Limit As Integer = 9
            Module M
             Sub Main()
              PrintLine(CStr(Limit))
             End Sub
            End Module
            """, "9");

    /// <summary>The access change must not lose a Module's own Private members inside it.</summary>
    [Test]
    public void AModulesOwnPrivateGlobalAndProcedure_StayReachableInsideIt_OnEveryBackend()
        => RunsOnEveryBackend("""
            Module M
             Private Count As Integer = 5
             Private Function Inner() As Integer
              Return 6
             End Function
             Public Function Get5() As Integer
              Return Count
             End Function
             Public Function Outer() As Integer
              Return Inner()
             End Function
            End Module
            Sub Main()
             PrintLine(CStr(M.Get5()))
             PrintLine(CStr(M.Outer()))
            End Sub
            """, "5\n6");

    // ------------------------------------------------------------------ what stays bare

    /// <summary>
    /// Inside the file module's own class the spelling stays bare: a call from <c>Main</c> and
    /// from another file-scope function, and a lambda in <c>Main</c> (MSIL has no <c>Func</c>).
    /// </summary>
    [Test]
    public void MainAndAFileScopeFunction_CallingAFileScopeFunction_StayBare_AndRunOnEveryBackend()
    {
        var program = FnTwice + """
            Function Quad(n As Integer) As Integer
             Return Twice(Twice(n))
            End Function
            Sub Main()
             PrintLine(CStr(Twice(4)))
             PrintLine(CStr(Quad(2)))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(CsText(program), Does.Contain("Twice(4)").And.Not.Contain(FileClass + ".Twice("),
                "bare inside the file module's own class");
            RunsOnEveryBackend(program, "8\n8");
        });
    }

    [Test]
    public void ALambdaInMain_CallingAFileScopeFunction_StaysBare_AndRunsOnThree()
    {
        var program = FnTwice + """
            Sub Main()
             Dim f As Func(Of Integer, Integer) = Function(x As Integer) Twice(x)
             PrintLine(CStr(f(4)))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(CsText(program), Does.Not.Contain(FileClass + ".Twice("));
            Assert.That(Cpp(program), Is.EqualTo("8"), "C++");
            Assert.That(Js(program), Is.EqualTo("8"), "JavaScript");
            Assert.That(Cs(program), Is.EqualTo("8"), "C#");
        });
    }

    /// <summary>
    /// ⛔ A class's OWN method, whatever symbol the analyzer bound the bare name to: declared
    /// above its caller (the class-scope symbol), below it (pass 1's global-scope stand-in),
    /// and each of those beside a same-named FILE-SCOPE function declared above the class,
    /// where the global-scope symbol IS the file-scope function's. Every backend printed the
    /// member's 3 before the change and prints it after; C# spells it bare.
    /// </summary>
    [TestCase(true, false, TestName = "{m}(declared above the caller)")]
    [TestCase(false, false, TestName = "{m}(declared below the caller)")]
    [TestCase(true, true, TestName = "{m}(above, beside a file-scope twin)")]
    [TestCase(false, true, TestName = "{m}(below, beside a file-scope twin)")]
    public void AClassMethod_CallingItsOwnMethod_StaysBare_AndRunsOnEveryBackend(bool declaredAbove, bool fileScopeTwin)
    {
        const string helper = " Public Function Helper() As Integer\n  Return 3\n End Function\n";
        const string run = " Public Function Run() As Integer\n  Return Helper()\n End Function\n";
        var program = (fileScopeTwin ? "Function Helper() As Integer\n Return 100\nEnd Function\n" : "")
            + "Class Box\n" + (declaredAbove ? helper + run : run + helper) + "End Class\n" + MainBox;
        Assert.Multiple(() =>
        {
            Assert.That(CsText(program), Does.Contain("Helper()").And.Not.Contain(FileClass + ".Helper("), "the member, bare");
            RunsOnEveryBackend(program, "3");
        });
    }

    /// <summary>An inherited method, bare. MSIL cannot find an inherited method yet (recorded).</summary>
    [Test]
    public void ADerivedClassMethod_CallingAnInheritedMethod_StaysBare_AndRunsOnThree()
    {
        const string program = """
            Class Base
             Public Function Helper() As Integer
              Return 3
             End Function
            End Class
            Class Box
             Inherits Base
             Public Function Run() As Integer
              Return Helper()
             End Function
            End Class
            """ + MainBox;
        Assert.Multiple(() =>
        {
            Assert.That(CsText(program), Does.Contain("Helper()").And.Not.Contain(FileClass + ".Helper("));
            Assert.That(Cpp(program), Is.EqualTo("3"), "C++");
            Assert.That(Js(program), Is.EqualTo("3"), "JavaScript");
            Assert.That(Cs(program), Is.EqualTo("3"), "C#");
        });
    }

    /// <summary>A local or a Shared field of the class shadows the file-scope name; neither is a global.</summary>
    [Test]
    public void ALocalOrASharedField_ShadowingAFileScopeName_IsNotQualified_OnEveryBackend()
        => RunsOnEveryBackend(FnTwice + """
            Dim Total As Integer = 7
            Class Box
             Public Shared Total As Integer = 2
             Public Function Run() As Integer
              Dim Twice As Integer = 5
              Return Twice + Total
             End Function
            End Class
            """ + MainBox, "7");

    // ------------------------------------------------------------------ a contested name

    /// <summary>
    /// ⛔ BROKEN ON ALL FOUR before the change: a file-scope <c>F</c> beside <c>Module A</c>'s
    /// <c>F</c> is DECLARED owner-qualified (<c>Main_F</c>, so the two can coexist) but was
    /// CALLED as the bare <c>F</c> — a function nothing defined. The call now goes out under the
    /// declared name with the owner beside it.
    /// </summary>
    [Test]
    public void AFileScopeFunction_ContestedWithAModulesProcedure_RunsOnEveryBackend()
    {
        const string program = """
            Function F() As Integer
             Return 1
            End Function
            Module A
             Public Function F() As Integer
              Return 2
             End Function
            End Module
            Class Box
             Public Function Run() As Integer
              Return F() + A.F()
             End Function
            End Class
            """ + MainBox;
        Assert.Multiple(() =>
        {
            var calls = AllCalls(BuildIr(program));
            Assert.That(calls.Select(c => c.FunctionName), Does.Contain("T_F").And.Contain("A_F").And.Not.Contain("F"),
                "called as declared: owner-qualified, no bare F");
            Assert.That(calls.Single(c => c.FunctionName == "T_F").CalleeModule, Is.EqualTo("T"), "the file module is the owner");
            RunsOnEveryBackend(program, "3");
        });
    }

    /// <summary>The same contest against a Module VARIABLE of that name.</summary>
    [Test]
    public void AFileScopeFunction_ContestedWithAModulesVariable_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Function Count() As Integer
             Return 1
            End Function
            Module A
             Public Count As Integer = 2
            End Module
            Class Box
             Public Function Run() As Integer
              Return Count() + A.Count
             End Function
            End Class
            """ + MainBox, "3");

    // ------------------------------------------------------------------ the wire form

    /// <summary>
    /// What carries NO owner, pinned on the IR: a stdlib procedure (its IR name must stay the one
    /// the backends' tables know — a Module declaring the same name would otherwise turn it into
    /// <c>Main_PrintLine</c>) and a <c>Declare</c> (externs are emitted into whichever module
    /// class comes first, so no owner is right; a Declare from a class body stays CS0103 on C#,
    /// out of scope). A file-scope callee from the same body carries the file module.
    /// </summary>
    [Test]
    public void AStdlibCallAndADeclare_CarryNoOwner_WhereAFileScopeCalleeCarriesTheFileModule()
    {
        var ir = BuildIr("""
            Declare Function getpid Lib "libc" () As Integer
            Function Twice(n As Integer) As Integer
             Return n * 2
            End Function
            Class Box
             Public Function Run() As Integer
              PrintLine("x")
              Return Len("abcd") + getpid() + Twice(1)
             End Function
            End Class
            """ + MainBox);
        var calls = AllCalls(ir);
        string[] Owners(string name) => calls.Where(c => c.FunctionName == name).Select(c => c.CalleeModule).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(Owners("PrintLine"), Is.EqualTo(new string[] { null, null }), "stdlib Sub, from the method and from Main");
            Assert.That(Owners("Len"), Is.EqualTo(new string[] { null }), "stdlib Function");
            Assert.That(Owners("getpid"), Is.EqualTo(new string[] { null }), "Declare");
            Assert.That(Owners("Twice"), Is.EqualTo(new[] { "T" }), "file scope");
        });
    }

    // ------------------------------------------------------------------ a class in a namespace

    /// <summary>
    /// ⛔ Named namespaces are emitted AFTER the default one, which holds every module class;
    /// a class inside one is therefore written after the last module class, and the "which
    /// module class am I in" record must be CLEARED when that class ends, or this method's
    /// call is spelled bare as if it were still inside it. Asserted on the TEXT: the shape
    /// does not run on C# for a reason of its own, measured and pre-existing — the module
    /// classes go into the default namespace and the class into <c>App</c>, with no using
    /// between them (CS0246 <c>Box</c> from the Module's <c>Main</c>, CS0103 the file class
    /// from <c>App</c>). The other three run it.
    /// </summary>
    [Test]
    public void AClassInANamespace_CallingAFileScopeFunction_IsQualified_AndRunsOnThree()
    {
        var program = FnTwice + """
            Namespace App
             Class Box
              Public Function Run() As Integer
               Return Twice(4)
              End Function
             End Class
             Module M
              Sub Main()
               Dim b As New Box()
               PrintLine(CStr(b.Run()))
              End Sub
             End Module
            End Namespace
            """;
        Assert.Multiple(() =>
        {
            Assert.That(CsText(program), Does.Contain(FileClass + ".Twice(4)"),
                "qualified: the class is written after the module classes, outside every one of them");
            Assert.That(Cpp(program), Is.EqualTo("8"), "C++");
            Assert.That(Js(program), Is.EqualTo("8"), "JavaScript");
            Assert.That(Msil(program), Is.EqualTo("8"), "MSIL");
        });
    }

    // ------------------------------------------------------------------ multi-file

    private static string TempDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "BasicLang_CsFileScope_" + Path.GetRandomFileName())).FullName;

    /// <summary>
    /// ⛔ The multi-file twin: a class calling a file-scope function of ITS OWN file was CS0103
    /// while one calling an IMPORTED file's function ran (that callee arrived with its source
    /// module). Both from one body now, on every backend.
    /// </summary>
    [Test]
    public void AClassInAProjectFile_CallingItsOwnFilesFunction_AndAnImportedOne_RunsOnEveryBackend()
    {
        var dir = TempDir();
        try
        {
            var util = Path.Combine(dir, "Util.bas");
            var main = Path.Combine(dir, "Main.bas");
            File.WriteAllText(util, "Function Other() As Integer\n Return 1\nEnd Function\n");
            File.WriteAllText(main, "Import Util\n" + FnTwice +
                "Class Box\n Public Function Run() As Integer\n  Return Twice(4) + Other()\n End Function\nEnd Class\n" + MainBox);

            var result = new BasicCompiler().CompileProjectFiles(new[] { util, main });
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            var ir = result.CombinedIR!;
            var pipeline = new OptimizationPipeline();
            pipeline.AddStandardPasses();
            pipeline.Run(ir);

            Assert.Multiple(() =>
            {
                var cs = new CSharpCodeGenerator().Generate(ir);
                Assert.That(cs, Does.Contain("Program.Twice(4)").And.Contain("Util.Other()"),
                    "each callee qualified by its file's class (Main.bas's is spelled Program, as the class is)");
                Assert.That(Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo("9"), "C#");
                var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(ir);
                Assert.That(Norm(BclE2E.CompileRun(cpp)), Is.EqualTo("9"), "C++");
                Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(new JavaScriptCodeGenerator().Generate(ir))), Is.EqualTo("9"), "JavaScript");
                Assert.That(Norm(MsilHarness.RunIlExpectingSuccess(new MSILCodeGenerator().Generate(ir))), Is.EqualTo("9"), "MSIL");
            });
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ------------------------------------------------------------------ helpers

    private static IRModule BuildIr(string source)
    {
        // One AST for both passes: node symbols are keyed by node identity.
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join(" | ", parser.Errors.Select(e => e.ToString())));
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True, string.Join(" | ", analyzer.Errors.Select(e => e.Message)));
        return new IRBuilder(analyzer).Build(ast, "T");
    }

    private static System.Collections.Generic.List<IRCall> AllCalls(IRModule ir) =>
        ir.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).OfType<IRCall>().ToList();
}
