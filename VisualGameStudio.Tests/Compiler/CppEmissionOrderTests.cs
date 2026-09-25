using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using VisualGameStudio.Tests.Msil;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// What a CLASS BODY may reference on the C++ backend: free functions and globals declared
/// anywhere in the program — a Module's procedure or variable, a file-scope one — from a
/// method, a constructor, a Shared method; single-file and split emission alike.
///
/// <para>⛔ A class's methods are defined INLINE in its body, and an inline member body sees
/// only the namespace-scope names declared BEFORE the class. The emitter wrote the classes
/// first and the free-function prototypes and the globals after them, so every one of these
/// was "use of undeclared identifier" on this backend alone — <c>Twice(4)</c>, <c>Count</c>,
/// a Const, an array, a string, a struct global, an Optional or ByRef callee, from a
/// constructor, a property getter or a Shared method — while JavaScript, MSIL and C# ran the
/// same program. Measured, compiled and run, on all four, for each shape here.</para>
///
/// <para>⚠ The prototypes and <c>extern</c> declarations of the globals now precede the class
/// bodies, after the enums, delegates and interfaces a prototype may name; the definitions
/// keep their places (a struct global needs its complete type, a global's initializer needs
/// the globals before it). One shared helper serves the combined emission and the split
/// header, where <c>extern T g;</c> pairs with the existing <c>inline T g = …;</c>.</para>
///
/// <para>⛔ A SECOND, DISTINCT DEFECT, found while pairing those: the split header dropped a
/// global's declared initializer — <c>Public Count As Integer = 5</c> built through a
/// <c>.blproj</c> was <c>inline int32_t Count = {};</c> and read 0, the wrong number from a
/// build that reported success, on that path alone. The combined emission had this exact bug
/// fixed on 2026-09-17; the split site never got it. Fixed the same way; its own test below.</para>
///
/// <para>⚠ Every running case asserts all four backends. The FILE-SCOPE shapes were pinned on
/// C# when this fixture was written (that backend left a file-scope callee or global bare
/// inside a class body or a Module block — CS0103) and are promoted since the fix; the
/// qualification itself is <c>CsFileScopeQualificationTests</c>' subject. Four C++ gaps the
/// probe measured are pinned at the end so the next reader knows they are real and not this
/// ordering.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out; the split leg spawns a compiler
public class CppEmissionOrderTests
{
    private static string Norm(string s) => FourBackends.Norm(s);
    private static void RunsOnEveryBackend(string program, string expected) => FourBackends.RunsOnEveryBackend(program, expected);
    private static string CppText(string program) => BclE2E.CompileToCppOptimized(program);
    private static string Cpp(string program) => Norm(BclE2E.CompileRun(CppText(program)));
    private static string Js(string program) => Norm(JavaScriptExecutionTests.RunJs(program));
    private static string Msil(string program) => Norm(MsilHarness.RunExpectingSuccess(program));
    private static string Cs(string program) => Norm(FourBackends.RunEmittedCSharp(program));

    private const string HelpersTwice = """
        Module Helpers
         Public Function Twice(n As Integer) As Integer
          Return n * 2
         End Function
        End Module
        """;

    // ------------------------------------------------------------------ every backend

    /// <summary>The statement form: a Sub, bare and qualified, from a method.</summary>
    [Test]
    public void AClassMethod_CallingAModuleSub_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Module Helpers
             Public Sub Say(s As String)
              PrintLine(s)
             End Sub
            End Module
            Class Box
             Public Sub Run()
              Say("a")
              Helpers.Say("b")
             End Sub
            End Class
            Sub Main()
             Dim b As New Box()
             b.Run()
            End Sub
            """, "a\nb");

    /// <summary>A constructor body is an inline member body too.</summary>
    [Test]
    public void AConstructor_CallingAModuleProcedure_RunsOnEveryBackend()
        => RunsOnEveryBackend(HelpersTwice + """

            Class Box
             Public V As Integer
             Public Sub New()
              Dim t As Integer = Twice(21)
              V = t
             End Sub
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.V))
            End Sub
            """, "42");

    /// <summary>
    /// The same constructor writing the call result STRAIGHT to the field. This was pinned as a
    /// JavaScript gap (JavaScript printed 0, the other three 42). Master's JS store-of-call-result
    /// fix (#72) closed it, so it now runs on every backend, as the pin said it should.
    /// </summary>
    [Test]
    public void AConstructor_AssigningACallResultStraightToAField_RunsOnEveryBackend()
        => RunsOnEveryBackend(HelpersTwice + """

            Class Box
             Public V As Integer
             Public Sub New()
              V = Twice(21)
             End Sub
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.V))
            End Sub
            """, "42");

    [Test]
    public void ASharedMethod_CallingAModuleProcedure_RunsOnEveryBackend()
        => RunsOnEveryBackend(HelpersTwice + """

            Class Box
             Public Shared Function Run() As Integer
              Return Twice(8)
             End Function
            End Class
            Sub Main()
             PrintLine(CStr(Box.Run()))
            End Sub
            """, "16");

    /// <summary>The prototype carries the parameter the omitted Optional is filled for at the call.</summary>
    [Test]
    public void AClassMethod_CallingAModuleProcedure_WithAnOptionalParameter_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Module Helpers
             Public Function Add(a As Integer, Optional b As Integer = 5) As Integer
              Return a + b
             End Function
            End Module
            Class Box
             Public Function Run() As Integer
              Return Add(1) + Helpers.Add(1, 1)
             End Function
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Run()))
            End Sub
            """, "8");

    /// <summary>⛔ The global half: a Module variable, bare and qualified, from a method.</summary>
    [Test]
    public void AClassMethod_ReadingAModuleGlobal_BareAndQualified_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Module State
             Public Count As Integer = 5
            End Module
            Class Box
             Public Function Read() As Integer
              Return Count + State.Count
             End Function
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Read()))
            End Sub
            """, "10");

    /// <summary>A module Const is a global on this backend, so it needed the same declaration.</summary>
    [Test]
    public void AClassMethod_ReadingAModuleConst_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Module K
             Public Const Limit As Integer = 9
            End Module
            Class Box
             Public Function Read() As Integer
              Return Limit + K.Limit
             End Function
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Read()))
            End Sub
            """, "18");

    /// <summary>A sized array global: declared `extern std::vector&lt;int32_t&gt;`, allocated at its definition.</summary>
    [Test]
    public void AClassMethod_IndexingAModuleArray_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Module State
             Public Vals(3) As Integer
            End Module
            Class Box
             Public Function Read() As Integer
              Return Vals(1) + State.Vals(2)
             End Function
            End Class
            Sub Main()
             Vals(1) = 8
             State.Vals(2) = 1
             Dim b As New Box()
             PrintLine(CStr(b.Read()))
            End Sub
            """, "9");

    [Test]
    public void AClassMethod_ReadingAModuleString_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Module State
             Public Name As String = "x"
            End Module
            Class Box
             Public Function Read() As String
              Return Name & State.Name
             End Function
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(b.Read())
            End Sub
            """, "xx");

    // ------------------------------------------------------------------ C++ (and C#) where the others have their own gaps

    /// <summary>
    /// The prototype spells the ByRef parameter as <c>int32_t&amp;</c>. JavaScript refuses ByRef
    /// by design (BL7002) and MSIL fails any ByRef call (recorded); the two that can run it do.
    /// </summary>
    [Test]
    public void AClassMethod_CallingAModuleSub_ByRef_RunsOnCppAndCSharp()
    {
        const string program = """
            Module Helpers
             Public Sub Bump(ByRef n As Integer)
              n = n + 1
             End Sub
            End Module
            Class Box
             Public Function Run() As Integer
              Dim x As Integer = 4
              Bump(x)
              Helpers.Bump(x)
              Return x
             End Function
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Run()))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(Cpp(program), Is.EqualTo("6"), "C++");
            Assert.That(Cs(program), Is.EqualTo("6"), "C#");
        });
    }

    /// <summary>
    /// A prototype taking and returning a <c>Structure</c> BY VALUE before the struct is complete:
    /// legal C++ for a declaration, which is why the forward declarations suffice. JavaScript
    /// refuses Structures by design (BL7005); MSIL cannot assemble this shape (recorded).
    /// </summary>
    [Test]
    public void AClassMethod_PassingAStructToAModuleProcedure_RunsOnCppAndCSharp()
    {
        const string program = """
            Structure Pt
             Dim X As Integer
            End Structure
            Module Helpers
             Public Function Bump(p As Pt) As Pt
              p.X = p.X + 1
              Return p
             End Function
            End Module
            Class Box
             Public Function Run() As Integer
              Dim p As Pt
              p.X = 4
              Dim q As Pt = Bump(p)
              Return q.X
             End Function
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Run()))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(Cpp(program), Is.EqualTo("5"), "C++");
            Assert.That(Cs(program), Is.EqualTo("5"), "C#");
        });
    }

    /// <summary>
    /// A global of a VALUE struct type: <c>extern Pt Origin;</c> before the class, with the struct
    /// still incomplete, and the definition after it where the type is complete.
    /// </summary>
    [Test]
    public void AClassMethod_ReadingAModuleStructGlobal_RunsOnCppAndCSharp()
    {
        const string program = """
            Structure Pt
             Dim X As Integer
            End Structure
            Module State
             Public Origin As Pt
            End Module
            Class Box
             Public Function Read() As Integer
              Return State.Origin.X
             End Function
            End Class
            Sub Main()
             State.Origin.X = 8
             Dim b As New Box()
             PrintLine(CStr(b.Read()))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(Cpp(program), Is.EqualTo("8"), "C++");
            Assert.That(Cs(program), Is.EqualTo("8"), "C#");
        });
    }

    /// <summary>
    /// The DEFINITIONS keep their order: <c>I</c> is initialized from <c>H</c>, which C++ honours
    /// only because the definitions are still written in declaration order below the classes.
    /// A declaration block that moved the definitions instead would silently break this.
    /// (JavaScript has no non-constant module initializer yet, by design.)
    /// </summary>
    [Test]
    public void AGlobalInitializedFromAnEarlierGlobal_ReadFromAMethod_RunsOnCppAndMsil()
    {
        const string program = """
            Dim H As Integer = 2
            Dim I As Integer = H
            Class Box
             Public Function Read() As Integer
              Return I
             End Function
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Read()))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(Cpp(program), Is.EqualTo("2"), "C++");
            Assert.That(Msil(program), Is.EqualTo("2"), "MSIL");
        });
    }

    // ------------------------------------------------------------------ file scope

    /// <summary>
    /// A FILE-SCOPE function from a method, declared before or after the class in source: the
    /// prototype now precedes the class either way. Was pinned on C# (CS0103: the callee went
    /// out bare inside the class); promoted with the qualification fix.
    /// </summary>
    [TestCase(true, TestName = "{m}(declared before the class)")]
    [TestCase(false, TestName = "{m}(declared after the class)")]
    public void AClassMethod_CallingAFileScopeFunction_RunsOnEveryBackend(bool declaredBefore)
    {
        const string fn = "Function Twice(n As Integer) As Integer\n Return n * 2\nEnd Function\n";
        const string cls = "Class Box\n Public Function Run() As Integer\n  Return Twice(4)\n End Function\nEnd Class\n";
        const string main = "Sub Main()\n Dim b As New Box()\n PrintLine(CStr(b.Run()))\nEnd Sub\n";
        RunsOnEveryBackend(declaredBefore ? fn + cls + main : cls + fn + main, "8");
    }

    /// <summary>Was pinned on C# (the write went out bare); promoted with the qualification fix.</summary>
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

    /// <summary>
    /// The C# gap was not about classes: a MODULE block calling a file-scope function was CS0103
    /// too, because the Module is its own static class. Promoted with the qualification fix.
    /// </summary>
    [Test]
    public void AModule_CallingAFileScopeFunction_RunsOnEveryBackend()
        => RunsOnEveryBackend("""
            Function Twice(n As Integer) As Integer
             Return n * 2
            End Function
            Module M
             Sub Main()
              PrintLine(CStr(Twice(4)))
             End Sub
            End Module
            """, "8");

    // ------------------------------------------------------------------ placement: after what a prototype may name

    /// <summary>
    /// A prototype naming an ENUM must follow the enum: <c>int32_t Code(Color c);</c> before
    /// <c>enum class Color</c> does not compile. This is what holds the block after the enums
    /// section rather than at the very top of the file. (The front end does not yet type an
    /// enum MEMBER assigned to a typed local or passed as an argument — measured, recorded — so
    /// the local carries the enum's default value and the body ignores it; the prototype's
    /// parameter type is the whole point.)
    /// </summary>
    [Test]
    public void AModuleProcedure_TakingAnEnum_CalledFromAMethod_RunsOnCpp()
        => Assert.That(Cpp("""
            Enum Color
             Red
             Green
            End Enum
            Module Helpers
             Public Function Code(c As Color) As Integer
              Return 7
             End Function
            End Module
            Class Box
             Public Function Run() As Integer
              Dim c As Color
              Return Code(c)
             End Function
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Run()))
            End Sub
            """), Is.EqualTo("7"));

    /// <summary>
    /// A prototype naming an INTERFACE must follow the interfaces section: interfaces are not
    /// forward-declared, so <c>std::shared_ptr&lt;IShape&gt;</c> needs the definition first.
    /// (A call THROUGH the interface is not typed by the front end yet, and neither is
    /// <c>Nothing</c> as an interface-typed argument — measured, recorded — so the body does
    /// not make one and the argument is a typed local; the prototype's parameter type is the
    /// point.)
    /// </summary>
    [Test]
    public void AModuleProcedure_TakingAnInterface_CalledFromAMethod_RunsOnCpp()
        => Assert.That(Cpp($"""
            Interface IShape
             Function Area() As Integer
            End Interface
            Class Sq
             Implements IShape
             Public Function Area() As Integer Implements IShape.Area
              Return 9
             End Function
            End Class
            Module Helpers
             Public Function Measure(s As IShape) As Integer
              Return 9
             End Function
            End Module
            Class Box
             Public Function Run() As Integer
              Dim s As IShape = New Sq()
              Return Measure(s)
             End Function
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Run()))
            End Sub
            """), Is.EqualTo("9"));

    // ------------------------------------------------------------------ the form, both emitters

    private const string OrderProgram = """
        Module State
         Public Count As Integer = 5
         Public Function Twice(n As Integer) As Integer
          Return n * 2
         End Function
        End Module
        Class Box
         Public Function Read() As Integer
          Return Twice(Count)
         End Function
        End Class
        Sub Main()
         Dim b As New Box()
         PrintLine(CStr(b.Read()))
        End Sub
        """;

    private static void AssertOrdered(string text, string what, params string[] markers)
    {
        var positions = markers.Select(m => (marker: m, at: text.IndexOf(m, StringComparison.Ordinal))).ToList();
        Assert.Multiple(() =>
        {
            foreach (var (marker, at) in positions)
                Assert.That(at, Is.GreaterThanOrEqualTo(0), $"{what}: missing '{marker}'\n{text}");
            for (var i = 1; i < positions.Count; i++)
                Assert.That(positions[i].at, Is.GreaterThan(positions[i - 1].at),
                    $"{what}: '{positions[i].marker}' must come after '{positions[i - 1].marker}'");
        });
    }

    /// <summary>The combined emission: prototype and `extern` before the class, the definition after it.</summary>
    [Test]
    public void TheCombinedEmission_DeclaresPrototypesAndGlobalsBeforeTheClasses_AndDefinesGlobalsAfter()
    {
        var cpp = CppText(OrderProgram);
        // "class Box\n" is the DEFINITION; the forward declaration is "class Box;".
        AssertOrdered(cpp, "combined",
            "// Function declarations", "int32_t Twice(int32_t n);", "extern int32_t Count;",
            "// Classes", "class Box\n", "int32_t Count = 5;", "// Function implementations");
        Assert.That(Cpp(OrderProgram), Is.EqualTo("10"));
    }

    /// <summary>The split header: the same order, with the `inline` definition after the classes.</summary>
    [Test]
    public void TheSplitHeader_DeclaresPrototypesAndGlobalsBeforeTheClasses_AndDefinesGlobalsInlineAfter()
    {
        var r = Split(emitMain: true, ("App.bas", OrderProgram));
        var header = r.Files["Game.g.h"];
        AssertOrdered(header, "split header",
            "// Function declarations", "int32_t Twice(int32_t n);", "extern int32_t Count;",
            "// Classes", "class Box\n", "inline int32_t Count = 5;");
    }

    // ------------------------------------------------------------------ split emission, compiled and run

    private static CppSplitResult Split(bool emitMain, params (string name, string code)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = files.Select(f => { var p = Path.Combine(dir, f.name); File.WriteAllText(p, f.code); return p; }).ToList();
            var result = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp" }).CompileProjectFiles(paths);
            Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
            return new CppCodeGenerator().GenerateSplit(result.CombinedIR, "Game", result.Units.Select(u => u.IR).ToList(), emitMain);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static string SplitRun(params (string name, string code)[] files)
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        var r = Split(emitMain: true, files);
        return Norm(CppCompile.CompileAndRunFiles(r.Files, r.TranslationUnitFileNames, compiler.Value));
    }

    [Test]
    public void Split_AClassMethodCallingAModuleProcedure_CompilesAndRuns()
        => Assert.That(SplitRun(
            ("Helpers.bas", HelpersTwice),
            ("App.bas", "Class Box\n Public Function Run() As Integer\n  Return Twice(4)\n End Function\nEnd Class\nSub Main()\n Dim b As New Box()\n PrintLine(CStr(b.Run()))\nEnd Sub\n")),
            Is.EqualTo("8"));

    /// <summary>
    /// ⛔ The initializer defect's own test, with no class involved: a Module global with a
    /// declared value, read from <c>Main</c> through a split build. This printed 0.
    /// </summary>
    [Test]
    public void Split_AModuleGlobalWithADeclaredInitializer_IsInitialized_CompilesAndRuns()
        => Assert.That(SplitRun(
            ("State.bas", "Module State\n Public Count As Integer = 5\nEnd Module\n"),
            ("App.bas", "Sub Main()\n PrintLine(CStr(State.Count))\nEnd Sub\n")),
            Is.EqualTo("5"));

    /// <summary>Both defects at once: declared before the class, initialized, bumped from another TU, read from a method.</summary>
    [Test]
    public void Split_AClassMethodReadingAModuleGlobal_AcrossTwoTranslationUnits_CompilesAndRuns()
        => Assert.That(SplitRun(
            ("State.bas", "Module State\n Public Count As Integer = 5\n Public Function Bump() As Integer\n  Count = Count + 1\n  Return Count\n End Function\nEnd Module\n"),
            ("App.bas", "Class Box\n Public Function Read() As Integer\n  Return State.Count\n End Function\nEnd Class\nSub Main()\n State.Bump()\n Dim b As New Box()\n PrintLine(CStr(b.Read()))\nEnd Sub\n")),
            Is.EqualTo("6"));

    [Test]
    public void Split_AStructGlobalReadFromAMethod_CompilesAndRuns()
        => Assert.That(SplitRun(
            ("App.bas", "Structure Pt\n Dim X As Integer\nEnd Structure\nDim Origin As Pt\nClass Box\n Public Function Read() As Integer\n  Return Origin.X\n End Function\nEnd Class\nSub Main()\n Origin.X = 8\n Dim b As New Box()\n PrintLine(CStr(b.Read()))\nEnd Sub\n")),
            Is.EqualTo("8"));

    /// <summary>
    /// ⛔ Found by a SURVIVING mutation: the split header's block placed BEFORE the enums passed
    /// every split test, because none of them carried an enum-typed prototype. This one does.
    /// </summary>
    [Test]
    public void Split_AModuleProcedureTakingAnEnum_CalledFromAMethod_CompilesAndRuns()
        => Assert.That(SplitRun(
            ("Types.bas", "Enum Color\n Red\n Green\nEnd Enum\nModule Helpers\n Public Function Code(c As Color) As Integer\n  Return 7\n End Function\nEnd Module\n"),
            ("App.bas", "Class Box\n Public Function Run() As Integer\n  Dim c As Color\n  Return Code(c)\n End Function\nEnd Class\nSub Main()\n Dim b As New Box()\n PrintLine(CStr(b.Run()))\nEnd Sub\n")),
            Is.EqualTo("7"));

    /// <summary>The interface twin of the enum case above, for the same reason.</summary>
    [Test]
    public void Split_AModuleProcedureTakingAnInterface_CalledFromAMethod_CompilesAndRuns()
        => Assert.That(SplitRun(
            ("Types.bas", "Interface IShape\n Function Area() As Integer\nEnd Interface\nClass Sq\n Implements IShape\n Public Function Area() As Integer Implements IShape.Area\n  Return 9\n End Function\nEnd Class\nModule Helpers\n Public Function Measure(s As IShape) As Integer\n  Return 9\n End Function\nEnd Module\n"),
            ("App.bas", "Class Box\n Public Function Run() As Integer\n  Dim s As IShape = New Sq()\n  Return Measure(s)\n End Function\nEnd Class\nSub Main()\n Dim b As New Box()\n PrintLine(CStr(b.Run()))\nEnd Sub\n")),
            Is.EqualTo("9"));

    [Test]
    public void Split_AModuleArrayReadFromAMethod_CompilesAndRuns()
        => Assert.That(SplitRun(
            ("State.bas", "Module State\n Public Vals(3) As Integer\nEnd Module\n"),
            ("App.bas", "Class Box\n Public Function Read() As Integer\n  Return State.Vals(1)\n End Function\nEnd Class\nSub Main()\n State.Vals(1) = 8\n Dim b As New Box()\n PrintLine(CStr(b.Read()))\nEnd Sub\n")),
            Is.EqualTo("8"));

    // ------------------------------------------------------------------ pinned C++ gaps, measured here, not this change's

    /// <summary>
    /// ⛔ PINNED: a class using a LATER class's member. A's inline body needs B complete, and B is
    /// still only forward-declared — "member access into incomplete type". The reverse order
    /// runs on all four. The prototype fix cannot reach this; it needs out-of-line member
    /// definitions (or dependency-ordered classes). Recorded as a candidate.
    /// </summary>
    [Test]
    public void AClassUsingALaterClassMember_IsAnOrderingGapOnCpp_Pinned()
    {
        const string aThenB = "Class A\n Public Function F() As Integer\n  Dim b As New B()\n  Return b.G() + 1\n End Function\nEnd Class\nClass B\n Public Function G() As Integer\n  Return 2\n End Function\nEnd Class\nSub Main()\n Dim a As New A()\n PrintLine(CStr(a.F()))\nEnd Sub\n";
        const string bThenA = "Class B\n Public Function G() As Integer\n  Return 2\n End Function\nEnd Class\nClass A\n Public Function F() As Integer\n  Dim b As New B()\n  Return b.G() + 1\n End Function\nEnd Class\nSub Main()\n Dim a As New A()\n PrintLine(CStr(a.F()))\nEnd Sub\n";
        Assert.Multiple(() =>
        {
            Assert.That(() => Cpp(aThenB), Throws.Exception.With.Message.Contains("incomplete type"),
                "PINNED: if this compiles, promote it to a four-backend case");
            Assert.That(Js(aThenB), Is.EqualTo("3"), "JavaScript");
            Assert.That(Msil(aThenB), Is.EqualTo("3"), "MSIL");
            Assert.That(Cs(aThenB), Is.EqualTo("3"), "C#");
            RunsOnEveryBackend(bThenA, "3");
        });
    }

    /// <summary>
    /// ⛔ PINNED: a ReadOnly Property with a Get block is not reachable as <c>b-&gt;Doubled</c> on
    /// C++ ("no member named"); the other three print 12. A property-emission gap, not ordering.
    /// </summary>
    [Test]
    public void APropertyGetter_IsNotAMemberOnCpp_Pinned()
    {
        var program = HelpersTwice + """

            Class Box
             Public ReadOnly Property Doubled As Integer
              Get
               Return Twice(6)
              End Get
             End Property
            End Class
            Sub Main()
             Dim b As New Box()
             PrintLine(CStr(b.Doubled))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(() => Cpp(program), Throws.Exception.With.Message.Contains("no member named"),
                "PINNED: if this compiles, promote it to a four-backend case");
            Assert.That(Js(program), Is.EqualTo("12"), "JavaScript");
            Assert.That(Msil(program), Is.EqualTo("12"), "MSIL");
            Assert.That(Cs(program), Is.EqualTo("12"), "C#");
        });
    }

    /// <summary>
    /// ⛔ PINNED: <c>Me</c> passed to a free function taking the class — <c>this</c> is a raw
    /// pointer where the prototype wants <c>std::shared_ptr&lt;Box&gt;</c> ("no matching
    /// function"). JavaScript and C# print 5; MSIL cannot assemble the shape (recorded).
    /// </summary>
    [Test]
    public void MeAsAnArgumentToAModuleProcedure_IsAGapOnCpp_Pinned()
    {
        const string program = """
            Class Box
             Public V As Integer
             Public Function Twin() As Box
              Return Clone(Me)
             End Function
            End Class
            Module Helpers
             Public Function Clone(b As Box) As Box
              Dim c As New Box()
              c.V = b.V + 1
              Return c
             End Function
            End Module
            Sub Main()
             Dim b As New Box()
             b.V = 4
             PrintLine(CStr(b.Twin().V))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(() => Cpp(program), Throws.Exception.With.Message.Contains("no matching function"),
                "PINNED: if this compiles, promote it");
            Assert.That(Js(program), Is.EqualTo("5"), "JavaScript");
            Assert.That(Cs(program), Is.EqualTo("5"), "C#");
        });
    }

    /// <summary>
    /// ⛔ PINNED: a GENERIC free function is "unknown type name 'T'" on C++ even when called from
    /// <c>Main</c> — nothing to do with class bodies. JavaScript and C# print 7.
    /// </summary>
    [Test]
    public void AGenericFreeFunction_IsAGapOnCpp_EvenFromMain_Pinned()
    {
        const string program = """
            Function Same(Of T)(v As T) As T
             Return v
            End Function
            Sub Main()
             PrintLine(CStr(Same(Of Integer)(7)))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(() => Cpp(program), Throws.Exception.With.Message.Contains("unknown type name 'T'"),
                "PINNED: if this compiles, promote it");
            Assert.That(Js(program), Is.EqualTo("7"), "JavaScript");
            Assert.That(Cs(program), Is.EqualTo("7"), "C#");
        });
    }
}
