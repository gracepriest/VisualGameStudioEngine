using System;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan tasks 10-11 — BL7005 (value <c>Structure</c>) and BL7006 (operator overloading).
/// </summary>
[TestFixture]
public class JsValueTypeAndOperatorTests
{
    private static string Reject(string source)
    {
        var module = JsTestSupport.BuildModule(source);
        var ex = Assert.Throws<ForeignFeatureException>(
            () => new JavaScriptCodeGenerator().Generate(module));
        return ex!.Message;
    }

    // ------------------------------------------------------------- BL7005 Structure

    /// <summary>
    /// A <c>Structure</c> has value semantics: assigning or passing one copies it. JavaScript
    /// objects are references, so honouring that would mean a deep clone on every assignment
    /// and every call — emulation, which the capability line forbids.
    ///
    /// <para>Rejected at the DECLARATION, not merely at use sites. Declaring one is itself
    /// using a language feature with no JS equivalent, and a declaration-keyed rule has no
    /// blind spot: use-site detection would have to catch every expression position, and
    /// recon showed <c>New Point()</c> in argument position never binds to a declared slot at
    /// all. Reject early, reject completely.</para>
    /// </summary>
    [TestCase("Structure P\nPublic X As Integer\nEnd Structure\nSub Main()\nEnd Sub",
        TestName = "Structure_DeclaredButUnused")]
    [TestCase("Structure P\nPublic X As Integer\nEnd Structure\nSub Main()\nDim p As P\nEnd Sub",
        TestName = "Structure_UsedAsLocal")]
    [TestCase("Structure P\nPublic X As Integer\nEnd Structure\nSub F(p As P)\nEnd Sub\nSub Main()\nEnd Sub",
        TestName = "Structure_UsedAsParameter")]
    [TestCase("Structure P\nPublic X As Integer\nEnd Structure\nClass C\nPublic Origin As P\nEnd Class\nSub Main()\nEnd Sub",
        TestName = "Structure_UsedAsClassField")]
    public void Structure_IsRejected(string source)
    {
        var message = Reject(source);
        Assert.That(message, Does.Contain("BL7005"));
        Assert.That(message, Does.Contain("Class"), "must point the user at Class");
    }

    /// <summary>
    /// A plain Class must survive — it is the recommended replacement, so an over-broad
    /// BL7005 that also caught classes would leave the user nowhere to go.
    /// </summary>
    [Test]
    public void Class_IsNotRejectedAsAStructure()
    {
        var module = JsTestSupport.BuildModule(
            "Class C\nPublic X As Integer\nEnd Class\nSub Main()\nEnd Sub");

        Exception caught = null;
        try { new JavaScriptCodeGenerator().Generate(module); }
        catch (Exception e) { caught = e; }

        Assert.That(caught, Is.Not.InstanceOf<ForeignFeatureException>());
    }

    /// <summary>
    /// <c>Type … End Type</c> (the VB6 UDT) and <c>Union</c> are value aggregates that
    /// IRBuilder emits NO declaration for at all — <c>Visit(TypeNode)</c> and
    /// <c>Visit(UnionNode)</c> are no-ops. So a rule keyed on <c>IRClass.IsStruct</c> cannot
    /// see them, and today a UDT-typed variable reaches codegen with its type declared
    /// nowhere.
    ///
    /// <para>They remain detectable at USE sites, because the variable's TypeInfo still
    /// carries Kind=UserDefinedType / Kind=Union. That is what this pins — value semantics
    /// with no JS equivalent must be refused however the user spells them.</para>
    /// </summary>
    [TestCase("Type Rec\nPublic A As Integer\nEnd Type\nSub Main()\nDim r As Rec\nEnd Sub",
        TestName = "UserDefinedType_UsedAsLocal")]
    public void ValueAggregatesWithNoIrDeclaration_AreStillRejected(string source)
    {
        Assert.That(Reject(source), Does.Contain("BL7005"));
    }

    // ------------------------------------------------------------- BL7006 operators

    /// <summary>
    /// JavaScript has no operator overloading — <c>a + b</c> on two objects stringifies them.
    /// Emitting the class without its operator would compile and produce nonsense at runtime.
    ///
    /// <para>Detected on the IRFunction name. Recon established operators lower to a function
    /// named <c>op_XXX</c>, class-qualified as <c>V.op_Addition</c> — and that a BARE
    /// <c>op_Addition</c> is also reachable when an operator follows a nested class, because
    /// _currentClassName is cleared unconditionally. Both spellings must be caught.</para>
    /// </summary>
    [TestCase("+", TestName = "Operator_Addition")]
    [TestCase("-", TestName = "Operator_Subtraction")]
    [TestCase("*", TestName = "Operator_Multiply")]
    public void OperatorOverloading_IsRejected(string op)
    {
        var source =
            $"Class V\nPublic Shared Operator {op}(a As V, b As V) As V\nReturn a\nEnd Operator\nEnd Class\n" +
            "Sub Main()\nEnd Sub";

        var message = Reject(source);
        Assert.That(message, Does.Contain("BL7006"));
        Assert.That(message, Does.Contain("V"), "must name the class the operator is on");
    }

    /// <summary>
    /// An ordinary method must not be caught. The arm keys on the compiler-generated
    /// <c>op_</c> prefix, so a user method merely CONTAINING those letters — or named
    /// something like Stop_Add — has to survive.
    /// </summary>
    [TestCase("Class C\nPublic Sub Loop_Add()\nEnd Sub\nEnd Class\nSub Main()\nEnd Sub",
        TestName = "MethodContainingUnderscoreAdd")]
    [TestCase("Sub Operation()\nEnd Sub\nSub Main()\nEnd Sub",
        TestName = "FunctionNamedOperation")]
    public void OrdinaryMethods_AreNotRejectedAsOperators(string source)
    {
        var module = JsTestSupport.BuildModule(source);

        Exception caught = null;
        try { new JavaScriptCodeGenerator().Generate(module); }
        catch (Exception e) { caught = e; }

        Assert.That(caught, Is.Not.InstanceOf<ForeignFeatureException>());
    }
}
