using System;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan tasks 8-9 — BL7003 (<c>Long</c>) and BL7004 (<c>Char</c>).
///
/// <para><b>Long</b> is out because a JS number is a double: it loses integer precision
/// past 2^53, and BigInt — the only exact alternative — contaminates every arithmetic
/// expression it touches. <b>Char</b> is out because JS has no character type at all.</para>
///
/// <para><b>These fixtures are position-driven, not feature-driven.</b> Rejecting a banned
/// type in the obvious place (a local) is easy; the bug is a banned type sitting in a
/// position the walk never visits, which then emits silently-wrong JavaScript. Recon of
/// ModuleTypeWalker found it covers 12 declared positions but does NOT recurse
/// GenericArguments or ElementType, and never visits delegates or extern declarations at
/// all. Each case below pins one of those positions.</para>
/// </summary>
[TestFixture]
public class JsBannedTypeTests
{
    private static string Reject(string source)
    {
        var module = JsTestSupport.BuildModule(source);
        var ex = Assert.Throws<ForeignFeatureException>(
            () => new JavaScriptCodeGenerator().Generate(module));
        return ex!.Message;
    }

    // ---------------------------------------------------------------- BL7003 Long

    [TestCase("Sub Main()\nDim n As Long\nEnd Sub", TestName = "Long_AsLocal")]
    [TestCase("Dim G As Long\nSub Main()\nEnd Sub", TestName = "Long_AsGlobal")]
    [TestCase("Sub F(n As Long)\nEnd Sub\nSub Main()\nEnd Sub", TestName = "Long_AsParameter")]
    [TestCase("Function F() As Long\nReturn 0\nEnd Function\nSub Main()\nEnd Sub", TestName = "Long_AsReturnType")]
    [TestCase("Class C\nPublic V As Long\nEnd Class\nSub Main()\nEnd Sub", TestName = "Long_AsClassField")]
    [TestCase("Interface I\nFunction F() As Long\nEnd Interface\nSub Main()\nEnd Sub", TestName = "Long_AsInterfaceReturn")]
    [TestCase("Interface I\nSub F(n As Long)\nEnd Interface\nSub Main()\nEnd Sub", TestName = "Long_AsInterfaceParameter")]
    public void Long_IsRejected_InEveryDeclaredPosition(string source)
    {
        Assert.That(Reject(source), Does.Contain("BL7003"));
    }

    /// <summary>
    /// ModuleTypeWalker yields only the TOP-LEVEL TypeInfo and explicitly leaves recursion
    /// to its callers (ModuleTypeWalker.cs:9-10). A checker that forgets to recurse accepts
    /// every one of these while rejecting the plain local — the worst kind of half-guard,
    /// because it looks like it works.
    /// </summary>
    [TestCase("Sub Main()\nDim a() As Long\nEnd Sub", TestName = "Long_AsArrayElementType")]
    [TestCase("Sub Main()\nDim l As New List(Of Long)()\nEnd Sub", TestName = "Long_AsGenericArgument")]
    public void Long_IsRejected_WhenNestedInsideAnotherType(string source)
    {
        Assert.That(Reject(source), Does.Contain("BL7003"));
    }

    /// <summary>
    /// Was [Ignore]d while a class <c>Property</c> could not parse at all: BasicLang had no
    /// auto-properties, so <c>Public Property V As Long</c> was a parse error, Parse()
    /// recovered by discarding the rest of the file, and there was no IR for BL7003's
    /// <c>Classes[].Properties[].Type</c> walk to find. Auto-properties now exist, so the
    /// position is live and this asserts the walk really covers it.
    /// </summary>
    [Test]
    public void Long_AsClassProperty_IsRejected()
    {
        Assert.That(Reject("Class C\nPublic Property V As Long\nEnd Class\nSub Main()\nEnd Sub"),
            Does.Contain("BL7003"));
    }

    // ---------------------------------------------------------------- BL7004 Char

    [TestCase("Sub Main()\nDim c As Char\nEnd Sub", TestName = "Char_AsLocal")]
    [TestCase("Sub F(c As Char)\nEnd Sub\nSub Main()\nEnd Sub", TestName = "Char_AsParameter")]
    [TestCase("Function F() As Char\nEnd Function\nSub Main()\nEnd Sub", TestName = "Char_AsReturnType")]
    [TestCase("Class C\nPublic V As Char\nEnd Class\nSub Main()\nEnd Sub", TestName = "Char_AsClassField")]
    [TestCase("Sub Main()\nDim a() As Char\nEnd Sub", TestName = "Char_AsArrayElementType")]
    public void Char_IsRejected(string source)
    {
        var message = Reject(source);
        Assert.That(message, Does.Contain("BL7004"));
        Assert.That(message, Does.Contain("String"), "must point the user at String");
    }

    /// <summary>
    /// The .NET spellings resolve to the same 64-bit / character types and must be refused
    /// identically. A position-complete checker that only knows the BasicLang spelling is
    /// still wrong: <c>Using System</c> makes <c>Int64</c> and <c>System.Char</c> ordinary
    /// declarations in fully-walked positions.
    /// </summary>
    [TestCase("Sub Main()\nDim a As Int64\nEnd Sub", "BL7003", TestName = "Int64_Spelling")]
    [TestCase("Sub Main()\nDim a As System.Int64\nEnd Sub", "BL7003", TestName = "SystemInt64_Spelling")]
    [TestCase("Sub Main()\nDim a As ULong\nEnd Sub", "BL7003", TestName = "ULong_SameDefect")]
    [TestCase("Sub Main()\nDim a As UInt64\nEnd Sub", "BL7003", TestName = "UInt64_Spelling")]
    [TestCase("Sub Main()\nDim c As System.Char\nEnd Sub", "BL7004", TestName = "SystemChar_Spelling")]
    public void DotNetSpellingsOfBannedTypes_AreAlsoRejected(string source, string code)
    {
        Assert.That(Reject(source), Does.Contain(code));
    }

    /// <summary>
    /// A banned type can reach the output carrying NO declared position at all, as a bare
    /// literal operand. <c>IRConstant</c> is never an entry in <c>block.Instructions</c> —
    /// every construction site assigns it to the expression result — so the declared-position
    /// walk is structurally blind to it.
    ///
    /// <para>Measured before this guard existed: <c>Console.WriteLine("a"c)</c> compiled clean
    /// and emitted <c>console.log(a);</c> — a bare undeclared JavaScript identifier, i.e. a
    /// ReferenceError in the browser from a build that reported success. That is exactly the
    /// silent-wrong-output class this backend exists to refuse.</para>
    /// </summary>
    [Test]
    public void CharLiteral_IsRejected_EvenWithNoDeclaredPosition()
    {
        Assert.That(Reject("Sub Main()\nConsole.WriteLine(\"a\"c)\nEnd Sub"), Does.Contain("BL7004"));
    }

    /// <summary>
    /// Class events were a measured MISS: <c>Public Event Tick As Long</c> compiled clean
    /// with no BL7003. <c>IRClass.Events</c> was not walked, and it cannot be backstopped by
    /// ModuleTypeWalker either — an event carries only a delegate type NAME, not a TypeInfo,
    /// so the shared walker explicitly skips it.
    /// </summary>
    [TestCase("Long", "BL7003", TestName = "Event_AsLong")]
    [TestCase("Char", "BL7004", TestName = "Event_AsChar")]
    [TestCase("Stream", "BL7007", TestName = "Event_AsBclType")]
    public void Event_CarryingABannedType_IsRejected(string type, string code)
    {
        var source = $"Class W\nPublic Event Tick As {type}\nEnd Class\nSub Main()\nEnd Sub";

        Assert.That(Reject(source), Does.Contain(code));
    }

    // ---------------------------------------------------------------- controls

    /// <summary>
    /// The supported numeric types must survive. A rejection arm that over-matches on
    /// "contains Int" or similar would take Integer with it and break every program.
    /// </summary>
    [TestCase("Sub Main()\nDim n As Integer\nEnd Sub", TestName = "Integer_IsAccepted")]
    [TestCase("Sub Main()\nDim d As Double\nEnd Sub", TestName = "Double_IsAccepted")]
    [TestCase("Sub Main()\nDim s As Single\nEnd Sub", TestName = "Single_IsAccepted")]
    [TestCase("Sub Main()\nDim b As Boolean\nEnd Sub", TestName = "Boolean_IsAccepted")]
    [TestCase("Sub Main()\nDim s As String\nEnd Sub", TestName = "String_IsAccepted")]
    public void SupportedTypes_AreNotRejected(string source)
    {
        var module = JsTestSupport.BuildModule(source);

        Exception caught = null;
        try { new JavaScriptCodeGenerator().Generate(module); }
        catch (Exception e) { caught = e; }

        Assert.That(caught, Is.Not.InstanceOf<ForeignFeatureException>(),
            "this type is IN for JavaScript and must not be rejected");
    }
}
