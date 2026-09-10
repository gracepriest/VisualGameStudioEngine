using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>MyBase.New(args)</c> — the base-constructor call — had NEVER parsed, on any backend.
///
/// <para><b>Measured:</b> <c>Error at line 4, column 16 in Class 'MyError': Expected 'New' after
/// MyBase. but found Identifier</c>. The lexer demotes every keyword that follows a <c>.</c> to
/// an Identifier so that <c>obj.Property</c>-style member names can be keywords
/// (BasicLangLexer.cs, "After a dot, treat everything as an identifier"), and
/// <c>ParseConstructor</c> demanded <c>TokenType.New</c> after <c>MyBase.</c>. The two rules
/// can never both hold, so the <c>MyBase.New</c> branch was dead code from the day it was
/// written — no test in the repository used the construct, which is how it stayed dead.</para>
///
/// <para>The whole pipeline below the parser was already built for it: the analyzer validates
/// base-constructor arity, IRBuilder carries <c>BaseConstructorArgs</c>, and every backend
/// emits <c>: base(…)</c> / <c>super(…)</c> / the C++ initializer. Only the entrance was
/// bricked up.</para>
/// </summary>
[TestFixture]
public class BaseConstructorCallTests
{
    private const string Source =
        "Class Animal\nPublic Name As String\n" +
        "Public Sub New(n As String)\nName = n\nEnd Sub\nEnd Class\n" +
        "Class Dog\nInherits Animal\n" +
        "Public Sub New(n As String)\nMyBase.New(n)\nEnd Sub\nEnd Class\n" +
        "Sub Main()\nDim d As New Dog(\"rex\")\nConsole.WriteLine(d.Name)\nEnd Sub";

    [Test]
    public void MyBaseNew_Parses_AndReachesTheJavaScriptSuperCall()
        => Assert.That(JsTestSupport.Compile(Source), Does.Contain("super(n);"));

    [Test]
    public void MyBaseNew_Parses_AndReachesTheCSharpBaseCall()
    {
        var module = JsTestSupport.BuildModule(Source);
        var cs = new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator().Generate(module);

        Assert.That(cs, Does.Contain(": base(n)"));
    }

    /// <summary>Case-insensitive, like every other keyword: the demoted identifier is compared by name.</summary>
    [TestCase("MyBase.new(n)")]
    [TestCase("MyBase.NEW(n)")]
    [TestCase("mybase.New(n)")]
    public void MyBaseNew_IsCaseInsensitive(string spelling)
        => Assert.That(JsTestSupport.Compile(Source.Replace("MyBase.New(n)", spelling)),
            Does.Contain("super(n);"));

    /// <summary>The arity check the analyzer already had, now reachable: a base with a 1-arg ctor and a 2-arg call.</summary>
    [Test]
    public void MyBaseNew_WithTheWrongArity_IsStillAnError()
        => Assert.That(() => JsTestSupport.Compile(Source.Replace("MyBase.New(n)", "MyBase.New(n, n)")),
            Throws.Exception.With.Message.Contains("No constructor for base class"));
}

/// <summary>And it runs.</summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class BaseConstructorCallExecutionTests
{
    [Test]
    public void TheBaseConstructorActuallyRuns()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Class Animal\nPublic Name As String\n" +
            "Public Sub New(n As String)\nName = n\nEnd Sub\nEnd Class\n" +
            "Class Dog\nInherits Animal\n" +
            "Public Sub New(n As String)\nMyBase.New(n)\nEnd Sub\nEnd Class\n" +
            "Sub Main()\nDim d As New Dog(\"rex\")\nConsole.WriteLine(d.Name)\nEnd Sub"),
            Is.EqualTo("rex"));
}
