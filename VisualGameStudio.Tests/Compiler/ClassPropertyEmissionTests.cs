using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A class <c>Property</c> does not swallow anything around it — chip task_40ab5ff8
/// ("class Property empties module").
///
/// <para><b>The chip did NOT reproduce.</b> Eight shapes were compiled and, where possible,
/// RUN: a full Get/Set property and an auto-property; a class inside a <c>Module</c>; a
/// <c>.mod</c> file; top-level statements (which BasicLang does not have); and the multi-file
/// project route through the real CLI — on the C#, C++ and JavaScript backends. Every member
/// survived in every one. This fixture pins those shapes so the question does not have to be
/// re-answered from scratch, and so a future regression is caught at the shape level.</para>
///
/// <para><b>What the investigation DID find</b> is a latent trap rather than a live bug.
/// <c>IRBuilder.EmitInstruction</c> discards an instruction outright when
/// <c>_currentBlock</c> is null — no error, no warning — and <c>Visit(PropertyNode)</c> used to
/// end by NULLING the build context where every sibling that creates a nested function saves
/// and restores it. It is unreachable today only because BasicLang has no top-level statements
/// and no function-local classes, so a property is never visited mid-function. It now restores,
/// which costs two locals. ⛔ No test here can reach that path — a test that cannot fail is
/// worse than none — so the hardening is deliberately covered by reasoning, not by a fake test.</para>
/// </summary>
[TestFixture]
public class ClassPropertyEmissionTests
{
    private const string FullProperty =
        "Public Class Box\n" +
        "Private _x As Integer\n" +
        "Public Property X As Integer\n" +
        "Get\nReturn _x\nEnd Get\n" +
        "Set(value As Integer)\n_x = value\nEnd Set\n" +
        "End Property\n" +
        "End Class\n";

    private const string AutoProperty =
        "Public Class Tag\nPublic Property Name As String\nEnd Class\n";

    /// <summary>
    /// Members declared AFTER a class with a property must survive. This is the shape the chip
    /// describes — anything "emptied" would show up as a missing sibling.
    /// </summary>
    [TestCase("csharp")]
    [TestCase("cpp")]
    [TestCase("javascript")]
    public void ClassWithAProperty_DoesNotDropSiblingDeclarations(string backend)
    {
        var module = JsTestSupport.BuildModule(
            FullProperty + AutoProperty +
            "Sub Before()\nConsole.WriteLine(1)\nEnd Sub\n" +
            "Sub After()\nConsole.WriteLine(2)\nEnd Sub\n" +
            "Sub Main()\nEnd Sub");

        var generated = BasicLang.Compiler.Driver.Program.GenerateCode(module, backend);

        Assert.That(generated, Does.Contain("Before"), "a sibling BEFORE the property class");
        Assert.That(generated, Does.Contain("After"), "a sibling AFTER the property class");
        Assert.That(generated, Does.Contain("Box"));
        Assert.That(generated, Does.Contain("Tag"));
    }

    /// <summary>
    /// The IR itself keeps every declaration — asserted separately from emission so a failure
    /// says WHICH stage lost it rather than only that the output looked wrong.
    /// </summary>
    [Test]
    public void ClassWithAProperty_LeavesEveryDeclarationOnTheModule()
    {
        var module = JsTestSupport.BuildModule(
            FullProperty + AutoProperty +
            "Sub Before()\nEnd Sub\nSub After()\nEnd Sub\nSub Main()\nEnd Sub");

        Assert.That(module.Classes.Keys, Is.EquivalentTo(new[] { "Box", "Tag" }));
        Assert.That(module.Functions.Select(f => f.Name),
            Has.Some.EqualTo("Before").And.Some.EqualTo("After").And.Some.EqualTo("Main"));
    }

    /// <summary>
    /// A class with a property nested in a <c>Module</c>, with subs on both sides — the reading
    /// of "empties module" that points at the Module construct itself.
    /// </summary>
    [Test]
    public void ClassWithAPropertyInsideAModule_DoesNotDropTheModulesMembers()
    {
        var module = JsTestSupport.BuildModule(
            "Module Helpers\n" +
            "Public Sub Before()\nEnd Sub\n" +
            FullProperty +
            "Public Sub After()\nEnd Sub\n" +
            "End Module\n" +
            "Sub Main()\nEnd Sub");

        Assert.That(module.Functions.Select(f => f.Name),
            Has.Some.Contains("Before").And.Some.Contains("After"));
        Assert.That(module.Classes.Keys, Does.Contain("Box"));
    }

    /// <summary>
    /// The property's own accessors still reach the module as get_/set_ functions — the thing
    /// Visit(PropertyNode) exists to produce. Guards against "fixing" the context handling by
    /// accidentally skipping the accessor bodies.
    /// </summary>
    [Test]
    public void ClassWithAProperty_StillEmitsItsAccessors()
    {
        var module = JsTestSupport.BuildModule(FullProperty + "Sub Main()\nEnd Sub");

        Assert.That(module.Functions.Select(f => f.Name),
            Has.Some.EqualTo("Box.get_X").And.Some.EqualTo("Box.set_X"));

        var box = module.Classes["Box"];
        Assert.That(box.Properties.Single().Getter, Is.Not.Null);
        Assert.That(box.Properties.Single().Setter, Is.Not.Null);
    }
}
