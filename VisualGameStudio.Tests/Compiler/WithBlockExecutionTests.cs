using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>With … End With</c>, RUN. Until this fixture a With block over anything but a Module member
/// compiled clean and did nothing: the IR builder dropped every <c>.Prop = value</c> (the
/// assignment dispatch had no arm for the implicit member) and never declared its <c>__with</c>
/// carrier, on every backend (form-designer followup 2). That is why <c>RegionWriter</c> must
/// never emit <c>With</c> — and nothing in the compiler stopped anyone else.
///
/// <para>The fix is a desugaring in the PARSER: <c>.M</c> is an <see cref="ImplicitWithMemberNode"/>,
/// which IS a <see cref="MemberAccessExpressionNode"/> whose receiver is the With's own name (a
/// plain identifier or <c>Me</c>) or a carrier <c>Dim __withN = expr</c>. So assignments, calls,
/// compound assignments, .NET receivers and nested blocks all take the one path every
/// <c>b.M</c> already takes. Every row below asserts OUTPUT, because the defect was a green build
/// whose stores vanished.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class WithBlockExecutionTests
{
    private static string Norm(string s) => FourBackends.Norm(s);
    private static string Cpp(string program) => Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)));
    private static string Js(string program) => Norm(JavaScriptExecutionTests.RunJs(program));
    private static string Cs(string program) => Norm(FourBackends.RunEmittedCSharp(program));

    private const string BoxClass = """
        Class Inner
         Public V As Integer
        End Class
        Class Box
         Public W As Integer
         Public H As Integer
         Public Property Name As String
         Public Child As Inner
         Public Sub New()
          Child = New Inner()
         End Sub
         Public Function Area() As Integer
          Return W * H
         End Function
         Public Sub Grow(n As Integer)
          W = W + n
         End Sub
         Public Sub Fill()
          With Me
           .H = .H + 1
          End With
         End Sub
        End Class
        Function MakeBox() As Box
         PrintLine("made")
         Return New Box()
        End Function

        """;

    /// <summary>Field and property stores, a Sub call, a compound assignment, a nested With over a
    /// member, and <c>With Me</c> inside a method — the shapes a form's initialisation code uses.</summary>
    private const string Stores = BoxClass + """
        Sub Main()
         Dim b As New Box()
         With b
          .W = 3
          .H = 4
          .Name = "box"
          .Grow(1)
          .W += 1
          With .Child
           .V = .V + 7
          End With
         End With
         PrintLine(CStr(b.Area()))
         PrintLine(b.Name)
         PrintLine(CStr(b.Child.V))
         b.Fill()
         PrintLine(CStr(b.H))
        End Sub
        """;

    /// <summary>A With over a CALL evaluates it exactly once ("made" prints once) and every
    /// <c>.M</c> reaches the same object.</summary>
    private const string OverAnExpression = BoxClass + """
        Sub Main()
         With MakeBox()
          .W = 2
          .H = 6
          PrintLine(CStr(.Area()))
         End With
        End Sub
        """;

    [Test]
    public void StoresCallsAndNestedBlocks_RunOnEveryBackend()
        => FourBackends.RunsOnEveryBackend(Stores, "20\nbox\n7\n5");

    [Test]
    public void AWithOverAnExpression_EvaluatesItOnce_OnEveryBackend()
        => FourBackends.RunsOnEveryBackend(OverAnExpression, "made\n12");

    /// <summary>
    /// VB's rule for a Structure: <c>With p</c> over a VARIABLE writes the variable, not a copy —
    /// which is why a plain name is named directly instead of copied into a carrier. JavaScript
    /// refuses a Structure by design (BL7005), so this is C# and C++.
    /// </summary>
    [Test]
    public void AWithOverAStructureVariable_WritesTheVariable()
    {
        const string program = """
            Structure Pt
             Public X As Integer
             Public Y As Integer
            End Structure
            Sub Main()
             Dim p As Pt
             With p
              .X = 10
              .Y = 20
             End With
             PrintLine(CStr(p.X + p.Y))
            End Sub
            """;
        Assert.Multiple(() =>
        {
            Assert.That(Cs(program), Is.EqualTo("30"), "C#");
            Assert.That(Cpp(program), Is.EqualTo("30"), "C++");
        });
    }

    /// <summary>A .NET receiver: the member resolves through the same .NET path as <c>sb.Append</c>.</summary>
    [Test]
    public void AWithOverADotNetObject_CallsItsMembers_OnCSharp()
    {
        const string program = """
            Using System.Text
            Sub Main()
             Dim sb As New StringBuilder()
             With sb
              .Append("a")
              .Append("b")
             End With
             PrintLine(sb.ToString())
            End Sub
            """;
        Assert.That(Cs(program), Is.EqualTo("ab"));
    }

    /// <summary>The JavaScript leg alone, for a reader who runs only this fixture's fast rows:
    /// the same Stores program, through node.</summary>
    [Test]
    public void Stores_RunOnJavaScript()
        => Assert.That(Js(Stores), Is.EqualTo("20\nbox\n7\n5"));

    // ------------------------------------------------------------------ the desugaring itself

    private static ProgramNode Parse(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join(" | ", parser.Errors.Select(e => e.ToString())));
        return ast;
    }

    private static WithStatementNode FindWith(ProgramNode ast)
        => ast.Declarations.OfType<SubroutineNode>().First().Body.Statements.OfType<WithStatementNode>().First();

    /// <summary>The <c>.M</c> targets of the block's assignment statements, in order.</summary>
    private static System.Collections.Generic.List<ImplicitWithMemberNode> Targets(WithStatementNode with)
        => with.Body.Statements.OfType<AssignmentStatementNode>().Select(a => (ImplicitWithMemberNode)a.Target).ToList();

    [Test]
    public void APlainName_IsTheReceiver_AndNeedsNoCarrier()
    {
        var with = FindWith(Parse("Sub Main()\n Dim b As Object\n With b\n  .X = 1\n End With\nEnd Sub\n"));
        var member = Targets(with).Single();
        Assert.Multiple(() =>
        {
            Assert.That(with.Carrier, Is.Null);
            Assert.That(member.Object, Is.InstanceOf<IdentifierExpressionNode>());
            Assert.That(((IdentifierExpressionNode)member.Object).Name, Is.EqualTo("b"));
        });
    }

    [Test]
    public void AnExpression_GetsACarrier_ThatEveryMemberReads()
    {
        var with = FindWith(Parse("Sub Main()\n With MakeIt()\n  .X = 1\n  .Y = 2\n End With\nEnd Sub\n"));
        var members = Targets(with);
        Assert.Multiple(() =>
        {
            Assert.That(with.Carrier, Is.Not.Null);
            Assert.That(with.Carrier.Initializer, Is.SameAs(with.Object), "the object is evaluated once, as the carrier's initializer");
            Assert.That(with.Carrier.IsAuto, Is.True, "typed from the initializer");
            Assert.That(members.Select(m => ((IdentifierExpressionNode)m.Object).Name), Is.All.EqualTo(with.Carrier.Name));
            Assert.That(members[0].Object, Is.Not.SameAs(members[1].Object), "a fresh receiver node per use — the analyzer keys by node");
        });
    }
}
