using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// An iterator's C# return type. BasicLang accepts VB's own spelling,
/// <c>Iterator Function F() As IEnumerable(Of Integer)</c>, and the element-type shorthand
/// <c>As Integer</c>. The backend wrapped the declared type unconditionally, so VB's spelling
/// became <c>IEnumerable&lt;IEnumerable&lt;int&gt;&gt;</c> and every <c>Yield</c> failed with CS0029.
/// Only the shorthand is wrapped now, as WrapAsyncReturnType leaves a declared Task alone.
/// </summary>
[TestFixture]
public class CSharpIteratorReturnTypeTests
{
    private static string Cs(string source) =>
        new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator().Generate(JsTestSupport.BuildModule(source));

    private const string Program = @"
Iterator Function Vb(n As Integer) As IEnumerable(Of Integer)
    For i As Integer = 1 To n
        Yield i * 2
    Next
End Function

Iterator Function Triples(n As Integer) As Integer
    For i As Integer = 1 To n
        Yield i * 3
    Next
End Function

Class Bag
    Public Iterator Function Items() As IEnumerable(Of String)
        Yield ""a""
        Yield ""b""
    End Function
End Class

Sub Main()
    For Each v As Integer In Vb(2)
        Console.WriteLine(v)
    Next
    For Each t As Integer In Triples(2)
        Console.WriteLine(t)
    Next
    Dim b As New Bag()
    For Each x As String In b.Items()
        Console.WriteLine(x)
    Next
End Sub";

    [Test]
    public void VbsSpelling_IsNotWrappedAgain()
    {
        var cs = Cs(Program);
        Assert.That(cs, Does.Contain("static IEnumerable<int> Vb(int n)"), cs);
        Assert.That(cs, Does.Contain("IEnumerable<string> Items()"), cs);
        Assert.That(cs, Does.Not.Contain("IEnumerable<IEnumerable<"), cs);
    }

    [Test]
    public void TheElementTypeShorthand_IsStillWrapped()
        => Assert.That(Cs(Program), Does.Contain("static IEnumerable<int> Triples(int n)"));

    [Test, Category("Integration")]   // builds and runs with dotnet through the CLI
    public void AllThreeShapes_BuildAndRun()
        => Assert.That(CliTestHarness.CompileRunCSharp(Program).Replace("\r\n", "\n").TrimEnd('\n'),
            Is.EqualTo("2\n4\n3\n6\na\nb"));
}
