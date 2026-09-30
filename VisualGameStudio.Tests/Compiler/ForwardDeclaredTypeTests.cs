using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A class or interface used ABOVE its declaration. Three layers, each measured:
/// <list type="number">
/// <item><b>Analyzer — bases.</b> <c>BaseType</c> was set only when Visit(ClassNode) reached the
/// class body, so above it <c>Dim a As Animal = New Dog()</c>, <c>Return New Dog()</c> from an
/// <c>As Animal</c> function, an argument and a field assignment were all refused ("Cannot
/// assign value of type 'Dog' to variable of type 'Animal'"). Pass 1 now links each class to
/// its base (RegisterClassBases) — never closing a cycle, which several unguarded base-chain
/// walks would spin on; a cycle is refused (BC30257), where it used to compile.</item>
/// <item><b>Analyzer — interfaces.</b> An interface was not a type until its own visit:
/// <c>Implements IShape</c> above it was "Unknown interface 'IShape'", and a call through it
/// typed as Object (`s.Area() + 1` refused). Interfaces are now pre-registered with their
/// member signatures, and a class's <c>Implements</c> is linked in pass 1.</item>
/// <item><b>Backends — order.</b> Declaration order was the only class order, and held only
/// because the analyzer refused these programs. C++ then failed `class Sq : public Base` ahead
/// of `Base` (incomplete type) and JavaScript built clean and died on load with "Cannot access
/// 'Base' before initialization". Both now emit through <c>IRModule.ClassesBaseFirst</c>.</item>
/// </list>
/// </summary>
[TestFixture]
public class ForwardDeclaredTypeTests
{
    private const string Types = """

        Class Holder
            Public Pet As Animal
        End Class
        Class Dog
            Inherits Animal
            Implements IShape
            Public Function Area() As Integer Implements IShape.Area
                Return 4
            End Function
        End Class
        Class Animal
        End Class
        Interface IShape
            Function Area() As Integer
        End Interface
        """;

    private static string Errors(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        if (parser.Errors.Count > 0) return string.Join("; ", parser.Errors.Select(e => e.Message));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return string.Join("; ", analyzer.Errors.Select(e => e.Message));
    }

    [TestCase("Dim a As Animal = New Dog()")]
    [TestCase("Dim h As New Holder()\n    h.Pet = New Dog()")]
    [TestCase("Take(New Dog())")]
    [TestCase("Dim s As IShape = New Dog()")]
    [TestCase("Dim s As IShape = New Dog()\n    Dim n As Integer = s.Area() + 1")]
    [TestCase("Dim a As Animal = Make()")]
    public void AUseAboveTheDeclarations_Analyzes(string body)
        => Assert.That(Errors(
            "Sub Take(a As Animal)\nEnd Sub\nFunction Make() As Animal\n    Return New Dog()\nEnd Function\n" +
            "Sub Main()\n    " + body + "\nEnd Sub" + Types), Is.Empty);

    /// <summary>Three levels declared bottom-up, each above its base.</summary>
    [Test]
    public void AChainDeclaredInReverse_Analyzes()
        => Assert.That(Errors(
            "Sub Main()\n    Dim a As A = New C()\nEnd Sub\n" +
            "Class C\n    Inherits B\nEnd Class\nClass B\n    Inherits A\nEnd Class\nClass A\nEnd Class"), Is.Empty);

    [TestCase("Class A\n    Inherits B\nEnd Class\nClass B\n    Inherits A\nEnd Class",
        "Class 'B' cannot inherit from itself: 'A' already inherits from 'B'")]
    [TestCase("Class A\n    Inherits A\nEnd Class", "Class 'A' cannot inherit from itself")]
    public void AnInheritanceCycle_IsRefused_AndTerminates(string decls, string error)
        => Assert.That(Errors(decls + "\nSub Main()\n    Dim a As A\nEnd Sub"), Does.Contain(error));

    /// <summary>Pre-registration is CONSUMED by the first declaration, so a real duplicate still reports.</summary>
    [TestCase("Class Box\nEnd Class\nClass Box\nEnd Class", "Class 'Box' is already defined")]
    [TestCase("Interface I\nEnd Interface\nInterface I\nEnd Interface", "Interface 'I' is already defined")]
    [TestCase("Class Sq\n    Implements INope\nEnd Class", "Unknown interface 'INope'")]
    [TestCase("Class Sq\n    Inherits Nope\nEnd Class", "Unknown base class 'Nope'")]
    public void TheOldDiagnostics_StillReport(string decls, string error)
        => Assert.That(Errors(decls + "\nSub Main()\nEnd Sub"), Does.Contain(error));

    /// <summary>Linked in pass 1 AND revisited in pass 2 — the interface is listed once.</summary>
    [Test]
    public void AnInterface_IsLinkedOnce()
    {
        var parser = new Parser(new Lexer("Sub Main()\nEnd Sub" + Types).Tokenize());
        var ast = parser.Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        var dog = new IRBuilder(analyzer).Build(ast, "M", "m.bas");
        Assert.That(analyzer.Errors, Is.Empty);
        Assert.That(dog.Classes["Dog"].Interfaces.Count(i => i == "IShape"), Is.EqualTo(1));
    }

    [Test]
    public void ClassesBaseFirst_PutsEveryBaseBeforeItsDerivedClasses()
    {
        var module = CSharpTestSupport.BuildModule(
            "Class C\n    Inherits B\nEnd Class\nClass X\nEnd Class\nClass B\n    Inherits A\nEnd Class\n" +
            "Class A\nEnd Class\nSub Main()\nEnd Sub");
        var order = module.ClassesBaseFirst().Select(c => c.Name).ToList();
        Assert.That(order, Is.EqualTo(new[] { "A", "B", "C", "X" }));
    }

    [Test]
    public void Cpp_EmitsTheBaseClassBodyFirst()
    {
        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(InterpolatedStringLoweringTests.Optimized(
                "Class Sq\n    Inherits Base\nEnd Class\nClass Base\nEnd Class\nSub Main()\n    Dim b As Base = New Sq()\nEnd Sub"));
        // The class DEFINITION lines, not the `class X;` forward declarations (Base's header
        // carries `: public std::enable_shared_from_this<Base>`, so match the line start).
        static int DefinitionOf(string cpp, string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(cpp, $@"^class {name}\b(?!;)",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            Assert.That(m.Success, Is.True, $"no definition of class {name}");
            return m.Index;
        }
        Assert.That(DefinitionOf(cpp, "Base"), Is.LessThan(DefinitionOf(cpp, "Sq")));
    }
}

[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class ForwardDeclaredTypeExecutionTests
{
    private const string Program = """
        Function Make(k As Integer) As Animal
            If k = 1 Then Return New Dog()
            Return New Cat()
        End Function

        Sub Speak(a As Animal)
            Console.WriteLine(a.Sound())
        End Sub

        Sub Show(s As IShape)
            Dim n As Integer = s.Area() + 1
            Console.WriteLine(n)
        End Sub

        Sub Main()
            Dim a As Animal = New Dog()
            Console.WriteLine(a.Sound())
            Speak(New Cat())
            Console.WriteLine(Make(2).Sound())
            Dim h As New Holder()
            h.Pet = New Puppy()
            Console.WriteLine(h.Pet.Sound() & " " & CStr(h.Pet.Legs))
            Dim s As IShape = New Sq()
            Show(New Sq())
            Console.WriteLine(s.Area())
            Console.WriteLine(TypeOf h.Pet Is Dog)
        End Sub

        Class Holder
            Public Pet As Animal
        End Class
        Class Puppy
            Inherits Dog
            Public Overrides Function Sound() As String
                Return "yip"
            End Function
        End Class
        Class Sq
            Implements IShape
            Public Function Area() As Integer Implements IShape.Area
                Return 4
            End Function
        End Class
        Class Dog
            Inherits Animal
            Public Overrides Function Sound() As String
                Return "woof"
            End Function
        End Class
        Class Cat
            Inherits Animal
            Public Overrides Function Sound() As String
                Return "meow"
            End Function
        End Class
        Class Animal
            Public Legs As Integer = 4
            Public Overridable Function Sound() As String
                Return "..."
            End Function
        End Class
        Interface IShape
            Function Area() As Integer
        End Interface
        """;

    private const string Expected = "woof\nmeow\nmeow\nyip 4\n5\n4\nTrue";

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    [Test]
    public void OnJavaScript() =>
        Assert.That(Normalize(JavaScriptExecutionTests.RunJs(Program)), Is.EqualTo(Expected));

    [Test]
    public void OnJavaScript_Optimized() =>
        Assert.That(Normalize(JavaScriptOptimizedExecutionTests.RunOptimized(Program)), Is.EqualTo(Expected));

    [Test]
    public void OnCpp()
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(InterpolatedStringLoweringTests.Optimized(Program));
        Assert.That(Normalize(VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(cpp, compiler.Value)),
            Is.EqualTo(Expected));
    }

    [Test]
    public void OnCSharp() =>
        Assert.That(Normalize(CliTestHarness.CompileRunCSharp(Program)), Is.EqualTo(Expected));
}
